using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using System.Linq;
using System.Reactive.Linq;
using Bonsai; // Requires dll for Bonsai.Core is exported with package
using Bonsai.Expressions; // Requires dll for Bonsai.Expressions is exported with package
using Bonsai.Dag;


/// <summary>
/// Hosts a Bonsai workflow inside Unity's process. The workflow is deserialized from a
/// <see cref="BonsaiWorkflowAsset"/> in <c>Awake</c> and, with <see cref="AutoStart"/> on, built and
/// subscribed in <c>Start</c> once every <see cref="HoldStart"/> has been released.
/// </summary>
/// <remarks>
/// Other components talk to the workflow only through the methods below, and every one of them must be
/// called before the workflow starts (in practice, from <c>Awake</c>):
/// <list type="bullet">
/// <item><see cref="InjectSubscription(string, Type, Action{object, long}, string)"/>: Bonsai to Unity, a callback on the main thread for each value a named node emits.</item>
/// <item><see cref="InjectImmediateSubscription"/>: the same, but called at once on Bonsai's thread, for handlers that can't wait for the next frame.</item>
/// <item><see cref="SendToSubject{T}"/>: Unity to Bonsai, pushes a <see cref="UnitySignal{T}"/>'s values into a subject the workflow declares.</item>
/// <item><see cref="InjectInput{T}"/>: Unity to Bonsai, feeds a source into an externalized property.</item>
/// <item><see cref="ReplaceSource{T}"/>: swaps a node (e.g. <c>KeyDown</c>) for a Unity-owned source.</item>
/// <item><see cref="SetNodeProperty"/>: overrides a node property (e.g. a <c>CsvWriter</c>'s <c>FileName</c>).</item>
/// </list>
/// By default these look at the top level of the workflow only. <see cref="InjectSubscription(string, Type, Action{object, long}, string)"/>,
/// <see cref="ReplaceSource{T}"/> and <see cref="SetNodeProperty"/> take an optional <c>groupPath</c> (e.g.
/// <c>"HARP"</c>) to reach the nodes inside a <c>GroupWorkflow</c> instead. Never inside a <c>SelectMany</c>.
/// Adapted from the unmaintained emotional-cities/unity package (https://github.com/emotional-cities/unity)
/// and fixed for Bonsai 2.9; see docs/developer/Bonsai-Internals.md.
/// </remarks>
public class BonsaiWorkflow : MonoBehaviour
{
    /// <summary>The workflow to run: the <c>.bonsai.asset</c> next to a <c>.bonsai</c> file, which the editor keeps up to date.</summary>
    public BonsaiWorkflowAsset BonsaiWorkflowAsset;

    /// <summary>Build and start the workflow in <c>Start</c>, once no component holds it (see <see cref="HoldStart"/>). This is currently the only way to start a workflow.</summary>
    public bool AutoStart = false;

    private WorkflowBuilder WorkflowBuilder;
    private IObservable<System.Reactive.Unit> WorkflowRuntime;
    private IDisposable WorkflowDisposable;

    // The set of Bonsai-->Unity subscriptions we must build when starting the workflow
    private List<SubscriptionInjection> SubscriptionInjections = new List<SubscriptionInjection>();

    // The set of Unity-->Bonsai input sources we must build when starting the workflow
    private List<InputInjection> InputInjections = new List<InputInjection>();

    // Source nodes in the workflow that Unity replaces with its own implementation when starting
    private List<SourceReplacement> SourceReplacements = new List<SourceReplacement>();

    // Node properties that Unity overrides when starting, e.g. where a CsvWriter saves its file
    private List<PropertyOverride> PropertyOverrides = new List<PropertyOverride>();

    // Unity-->Bonsai signals pushed into a named subject of the workflow when starting
    private List<SignalInjection> SignalInjections = new List<SignalInjection>();

    // Components that have asked AutoStart to wait for them (see HoldStart), and whether it's waiting
    private HashSet<object> StartHolds = new HashSet<object>();
    private bool AutoStartPending;

    private void Awake()
    {
        ParseWorkflow();
    }

    private void Start()
    {
        if (AutoStart)
        {
            AutoStartPending = true;
            TryAutoStart();
        }
    }

    /// <summary>
    /// Keeps <see cref="AutoStart"/> from starting the workflow until the same holder calls
    /// <see cref="ReleaseStart"/>, e.g. so that a session folder exists before any trial runs. Call from
    /// <c>Awake</c>, since AutoStart fires in <c>Start</c>.
    /// </summary>
    /// <param name="holder">Any object that identifies the caller; pass the same one to <see cref="ReleaseStart"/>.</param>
    public void HoldStart(object holder)
    {
        StartHolds.Add(holder);
    }

    /// <summary>
    /// Removes a hold placed with <see cref="HoldStart"/>. If it was the last one and <see cref="AutoStart"/>
    /// is waiting, the workflow starts immediately, on the caller's thread.
    /// </summary>
    /// <param name="holder">The object passed to <see cref="HoldStart"/>.</param>
    public void ReleaseStart(object holder)
    {
        StartHolds.Remove(holder);
        TryAutoStart();
    }

    private void TryAutoStart()
    {
        if (AutoStartPending && StartHolds.Count == 0)
        {
            AutoStartPending = false;
            StartWorkflow();
        }
    }

    private void StartWorkflow()
    {
        if (WorkflowDisposable != null)
        {
            Debug.LogError("Workflow already started.");
            return;
        }

        // Swap the requested source nodes for their Unity-side replacements before anything is built
        // This is here primarily to handle keypress events from Unity input.
        foreach (var replacement in SourceReplacements)
        {
            // Only the nodes feeding the named successor, so that two KeyDown nodes (Ctrl-J, Ctrl-E) can have different sources
            bool IsTarget(Node<ExpressionBuilder, ExpressionBuilderArgument> node) =>
                node.Successors.Any(edge => ExpressionBuilder.GetElementDisplayName(edge.Target.Value) == replacement.SuccessorName)
                && ExpressionBuilder.Unwrap(node.Value) is CombinatorBuilder builder && builder.Combinator != null
                && builder.Combinator.GetType().FullName == replacement.TargetTypeName;

            var nodes = NodesIn(WorkflowBuilder.Workflow, replacement.GroupPath).Where(IsTarget).ToList();
            string what = $"{replacement.TargetTypeName} node feeding '{replacement.SuccessorName}'";

            if (nodes.Count == 0)
            {
                nodes = FallbackSearch(IsTarget, what, replacement.GroupPath);
            }

            var builders = nodes.Select(node => (CombinatorBuilder)ExpressionBuilder.Unwrap(node.Value)).ToList();

            foreach (var builder in builders)
            {
                builder.Combinator = replacement.Source;
            }
        }

        // Apply property overrides. Combinator nodes are matched on the combinator they wrap, and other
        // nodes (CsvWriter is an expression builder, for example) on the node itself.
        foreach (var propertyOverride in PropertyOverrides)
        {
            var targets = NodesIn(WorkflowBuilder.Workflow, propertyOverride.GroupPath)
                .Select(node => ExpressionBuilder.Unwrap(node.Value))
                .Select(builder => builder is CombinatorBuilder combinatorBuilder ? combinatorBuilder.Combinator : builder)
                .Where(element => element != null && element.GetType().FullName == propertyOverride.TargetTypeName)
                .ToList();

            if (targets.Count == 0)
            {
                Debug.LogWarning($"No {propertyOverride.TargetTypeName} node found in the workflow to set {propertyOverride.PropertyName} on.");
            }

            foreach (var target in targets)
            {
                var property = target.GetType().GetProperty(propertyOverride.PropertyName);
                if (property == null || !property.CanWrite)
                {
                    Debug.LogError($"{propertyOverride.TargetTypeName} has no settable property {propertyOverride.PropertyName}.");
                    continue;
                }

                // Enum values can be given by name, so callers don't need a reference to the enum type
                var value = propertyOverride.Value;
                if (property.PropertyType.IsEnum && value is string enumName)
                {
                    value = Enum.Parse(property.PropertyType, enumName);
                }

                property.SetValue(target, value);
            }
        }

        // Wire each Unity signal to its subject: a source node for the signal feeding a MulticastSubject, the
        // node Bonsai itself uses to push values into a subject by name. Both go in the graph that declares
        // the subject, because a subject is only visible from its own level of the workflow and below.
        foreach (var signal in SignalInjections)
        {
            var graph = GraphAt(WorkflowBuilder.Workflow, signal.GroupPath);
            if (graph == null)
            {
                continue;   // GraphAt has already said which group is missing
            }

            // Checked here because a MulticastSubject with no subject to push into fails the whole build
            bool declared = graph.Any(node => !IsDisabled(node.Value)
                && ExpressionBuilder.Unwrap(node.Value) is SubjectExpressionBuilder subject && subject.Name == signal.SubjectName);

            if (!declared)
            {
                string where = string.IsNullOrEmpty(signal.GroupPath) ? "at the top level of the workflow" : $"in group {signal.GroupPath}";
                Debug.LogError($"No subject named {signal.SubjectName} is declared {where}, so Unity's signal to it is not connected. Add a typed BehaviorSubject or PublishSubject with that name there.", this);
                continue;
            }

            var source = graph.Add(new CombinatorBuilder { Combinator = signal.Source });
            var sink = graph.Add(new MulticastSubject { Name = signal.SubjectName });
            graph.AddEdge(source, sink, new ExpressionBuilderArgument());
        }

        // Apply input injections. TODO - what if user tries multiple inputs to the same node
        foreach (var input in InputInjections)
        {
            // Convert externalized mapping to property mapping
            var propertyMappingBuilder = new PropertyMappingBuilder();
            var externalizedMappingBuilder = ExpressionBuilder.Unwrap(input.To.Value) as ExternalizedMappingBuilder; // TODO - 'To' should always be an externalizedmapping or propertymapping
            foreach (var mapping in externalizedMappingBuilder.ExternalizedProperties)
            {
                propertyMappingBuilder.PropertyMappings.Add(new PropertyMapping(mapping.Name, null));
            }

            // replace externalized mapping with property mapping
            var replaceNode = WorkflowBuilder.Workflow.Add(propertyMappingBuilder);
            WorkflowBuilder.Workflow.AddEdge(replaceNode, input.To.Successors[0].Target, new ExpressionBuilderArgument());
            WorkflowBuilder.Workflow.Remove(input.To);

            // Add the input injection and edge
            var fromNode = WorkflowBuilder.Workflow.Add(input.From);
            WorkflowBuilder.Workflow.AddEdge(fromNode, replaceNode, new ExpressionBuilderArgument());
        }

        // Build workflow. Wrap in inspectable builders exactly once: ExpressionBuilder.Unwrap only
        // removes a single InspectBuilder, so a second wrap hides IRequireBuildContext elements
        // such as subjects and they never receive a build context.
        WorkflowBuilder = new WorkflowBuilder(WorkflowBuilder.Workflow.ToInspectableGraph());
        WorkflowBuilder.Workflow.Build();
        WorkflowRuntime = WorkflowBuilder.Workflow.BuildObservable();

        // Apply subscription injections
        foreach (var subscription in SubscriptionInjections)
        {
            // ToInspectableGraph recurses, so the nodes inside groups are InspectBuilders too
            bool IsTarget(Node<ExpressionBuilder, ExpressionBuilderArgument> node) =>
                ExpressionBuilder.GetElementDisplayName(node.Value) == subscription.TargetName
                && ExpressionBuilder.Unwrap(node.Value).GetType() == subscription.TargetType;

            var matches = NodesIn(WorkflowBuilder.Workflow, subscription.GroupPath).Where(IsTarget).ToList();

            if (matches.Count == 0)
            {
                matches = FallbackSearch(IsTarget, $"{subscription.TargetType.Name} {subscription.TargetName} for subscription injection", subscription.GroupPath);
            }

            var selectedNode = matches.FirstOrDefault();

            if (matches.Count > 1)
            {
                Debug.LogWarning($"{matches.Count} {subscription.TargetType.Name} nodes named {subscription.TargetName} found for subscription injection; using the first. Give them unique names.");
            }

            if (selectedNode != null)
            {
                var inspect = (InspectBuilder)selectedNode.Value;
                // The timestamp is taken here, on the thread that emitted, because the main thread may
                // not run the action until the next frame
                inspect.Output.Merge().Subscribe(unit =>
                {
                    var timestamp = System.Diagnostics.Stopwatch.GetTimestamp();

                    if (!subscription.Immediate)
                    {
                        BonsaiDispatcher.RunOnMainThread(() => { subscription.Action(unit, timestamp); });
                        return;
                    }

                    // On Bonsai's thread. An exception left to escape would travel back into the workflow
                    // and could end the sequence that emitted the value.
                    try
                    {
                        subscription.Action(unit, timestamp);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                });
            }
        }

        // Create workflow subscription to begin, cache the disposable for ending the session, log errors to the main thread
        WorkflowDisposable = WorkflowRuntime.Subscribe(
            unit => { },
            er => { BonsaiDispatcher.RunOnMainThread(() => { Debug.LogError(er, this); }); },
            () => { Debug.Log("Completed"); }
        );
    }

    /// <summary>
    /// Disposes the running workflow, which closes its serial ports and Arduino connection. Safe to call
    /// when it never started or has already been stopped. Trials and the Ctrl+J juice trigger stop working.
    /// </summary>
    public void StopWorkflow()
    {
        // Null if the workflow never started, e.g. when quitting from the session setup screen
        WorkflowDisposable?.Dispose();
        WorkflowDisposable = null;
    }

    private void OnDestroy()
    {
        StopWorkflow();

        // Play mode does not reload the scene, so this instance can be started again. Left over, the
        // old disposable makes StartWorkflow refuse ("already started") and the old registrations
        // would be applied a second time, doubling every callback. Components register again in Awake.
        SubscriptionInjections.Clear();
        InputInjections.Clear();
        SourceReplacements.Clear();
        PropertyOverrides.Clear();
        SignalInjections.Clear();
        StartHolds.Clear();
        AutoStartPending = false;
    }

    /// <summary>
    /// The nodes a hook searches. With no <paramref name="groupPath"/>, the top level of
    /// <paramref name="graph"/>, exactly as before group paths existed. Otherwise the nodes inside the named
    /// group, and inside any groups nested in it, skipping disabled groups.
    /// </summary>
    /// <remarks>
    /// Only <see cref="GroupWorkflowBuilder"/> is entered. <c>SelectMany</c> and <c>CreateObservable</c> derive
    /// from the same base class, but their inner workflows are built again for every element, so a subscription
    /// there never fires and a property set there is lost (the SelectMany gotcha in Bonsai-Internals.md).
    /// Inside a group, nodes are matched by type and display name like at the top level, so subject names
    /// should stay unique across the whole workflow.
    /// </remarks>
    /// <param name="graph">The top-level graph, raw before the build or inspectable after it.</param>
    /// <param name="groupPath">Group display names separated by <c>/</c>, e.g. <c>"HARP"</c> or <c>"HARP/Markers"</c>.</param>
    private static IEnumerable<Node<ExpressionBuilder, ExpressionBuilderArgument>> NodesIn(ExpressionBuilderGraph graph, string groupPath)
    {
        // A null string can come back as "" after a recompile, so both mean the top level
        if (string.IsNullOrEmpty(groupPath))
        {
            return graph;
        }

        graph = GraphAt(graph, groupPath);
        return graph == null
            ? Enumerable.Empty<Node<ExpressionBuilder, ExpressionBuilderArgument>>()
            : NodesWithNestedGroups(graph);
    }

    // The graph of the group a path names, or the given graph for no path. Null, with a warning, if a group
    // on the path is missing or disabled.
    private static ExpressionBuilderGraph GraphAt(ExpressionBuilderGraph graph, string groupPath)
    {
        if (string.IsNullOrEmpty(groupPath))
        {
            return graph;
        }

        foreach (var segment in groupPath.Split('/'))
        {
            var group = graph
                .Select(node => node.Value)
                .Where(builder => !IsDisabled(builder))
                .Select(builder => ExpressionBuilder.Unwrap(builder) as GroupWorkflowBuilder)
                .FirstOrDefault(builder => builder != null && ExpressionBuilder.GetElementDisplayName(builder) == segment);

            if (group == null)
            {
                Debug.LogWarning($"Group {segment} (in path {groupPath}) not found in the workflow; nothing there can be hooked.");
                return null;
            }

            graph = group.Workflow;
        }

        return graph;
    }

    /// <summary>
    /// For a hook that found nothing where its <c>groupPath</c> said: searches the whole workflow (top level and
    /// every group, never a <c>SelectMany</c>) and warns either way, so a workflow regrouped in Bonsai keeps
    /// working until the group name is added to the call. Not used by <see cref="SetNodeProperty"/>, which
    /// changes every match and would also hit nodes in other groups (the readout's and Harp's
    /// <c>CsvWriter</c>, for example).
    /// </summary>
    /// <param name="isTarget">The hook's own match test.</param>
    /// <param name="what">The target, as it should read in the warning.</param>
    /// <param name="groupPath">Where the hook looked.</param>
    /// <returns>The matches, all from the first group (in workflow order) that holds any; empty if none.</returns>
    private List<Node<ExpressionBuilder, ExpressionBuilderArgument>> FallbackSearch(
        Func<Node<ExpressionBuilder, ExpressionBuilderArgument>, bool> isTarget, string what, string groupPath)
    {
        string looked = string.IsNullOrEmpty(groupPath) ? "at the top level" : $"in group {groupPath}";
        var found = NodesEverywhere(WorkflowBuilder.Workflow, "").Where(x => isTarget(x.node)).ToList();

        if (found.Count == 0)
        {
            Debug.LogWarning($"{what} not found {looked}, or anywhere else in the workflow.");
            return new List<Node<ExpressionBuilder, ExpressionBuilderArgument>>();
        }

        // Matches in one group only, so one name in two places doesn't get hooked twice
        string path = found[0].path;
        string fix = path == "" ? "drop the groupPath" : $"pass groupPath \"{path}\"";
        Debug.LogWarning($"{what} not found {looked}; using the one {(path == "" ? "at the top level" : $"in group {path}")}. To silence this, {fix}.");
        return found.Where(x => x.path == path).Select(x => x.node).ToList();
    }

    // Every node in the workflow with the path of the group it sits in ("" for the top level), entering
    // GroupWorkflows only, as NodesIn does
    private static IEnumerable<(Node<ExpressionBuilder, ExpressionBuilderArgument> node, string path)> NodesEverywhere(ExpressionBuilderGraph graph, string path)
    {
        foreach (var node in graph)
        {
            yield return (node, path);

            if (!IsDisabled(node.Value) && ExpressionBuilder.Unwrap(node.Value) is GroupWorkflowBuilder group)
            {
                string name = ExpressionBuilder.GetElementDisplayName(group);
                foreach (var inner in NodesEverywhere(group.Workflow, path == "" ? name : $"{path}/{name}"))
                {
                    yield return inner;
                }
            }
        }
    }

    private static IEnumerable<Node<ExpressionBuilder, ExpressionBuilderArgument>> NodesWithNestedGroups(ExpressionBuilderGraph graph)
    {
        foreach (var node in graph)
        {
            yield return node;

            if (!IsDisabled(node.Value) && ExpressionBuilder.Unwrap(node.Value) is GroupWorkflowBuilder group)
            {
                foreach (var inner in NodesWithNestedGroups(group.Workflow))
                {
                    yield return inner;
                }
            }
        }
    }

    // Peeled by hand because ExpressionBuilder.Unwrap may also remove the DisableBuilder, hiding that the node is off
    private static bool IsDisabled(ExpressionBuilder builder)
    {
        while (builder is InspectBuilder inspect)
        {
            builder = inspect.Builder;
        }

        return builder is DisableBuilder;
    }

    private void ParseWorkflow()
    {
        using (var stringReader = new StringReader(BonsaiWorkflowAsset.WorkflowBuilderData))
        {
            using (var reader = XmlReader.Create(stringReader))
            {
                reader.MoveToContent();
                var serializer = new XmlSerializer(typeof(WorkflowBuilder), reader.NamespaceURI);
                WorkflowBuilder = (WorkflowBuilder)serializer.Deserialize(reader);
            }
        }
    }

    /// <summary>
    /// Calls <paramref name="action"/> on Unity's main thread with every value a named node emits (top level,
    /// or inside <paramref name="groupPath"/>). Must be called before the workflow starts, so from <c>Awake</c>.
    /// </summary>
    /// <param name="targetNode">The node's display name in the workflow, e.g. a subject's <c>Name</c> such as <c>"TouchOut"</c>.</param>
    /// <param name="targetType">
    /// The node's exact deserialized type, e.g. <c>typeof(Bonsai.Reactive.BehaviorSubject)</c>. Matching is by
    /// <c>GetType() ==</c>, and <c>SubscribeSubject</c> nodes often share the subject's name, so this is what
    /// picks the right node.
    /// </param>
    /// <param name="action">Receives each emitted value, boxed.</param>
    /// <param name="groupPath">Null for the top level, or a <c>GroupWorkflow</c>'s name (<c>/</c> for nested groups) to search inside it.</param>
    public void InjectSubscription(string targetNode, Type targetType, Action<object> action, string groupPath = null)
    {
        InjectSubscription(targetNode, targetType, (value, timestamp) => action(value), groupPath);
    }

    /// <summary>
    /// As <see cref="InjectSubscription(string, Type, Action{object}, string)"/>, but the action also receives the
    /// Stopwatch timestamp (QueryPerformanceCounter ticks) taken on Bonsai's thread the moment the node
    /// emitted, rather than when the main thread runs the action (up to a frame later).
    /// </summary>
    /// <param name="targetNode">The node's display name in the workflow.</param>
    /// <param name="targetType">The node's exact deserialized type.</param>
    /// <param name="action">Receives each emitted value, boxed, and its emission time in <see cref="System.Diagnostics.Stopwatch"/> ticks.</param>
    /// <param name="groupPath">Null for the top level, or a <c>GroupWorkflow</c>'s name (<c>/</c> for nested groups) to search inside it.</param>
    public void InjectSubscription(string targetNode, Type targetType, Action<object, long> action, string groupPath = null)
    {
        SubscriptionInjections.Add(new SubscriptionInjection { TargetName = targetNode, TargetType = targetType, Action = action, GroupPath = groupPath });
    }

    /// <summary>
    /// As <see cref="InjectSubscription(string, Type, Action{object, long}, string)"/>, but the action runs at
    /// once, on Bonsai's thread, instead of waiting for Unity's main thread (up to a frame). For the few
    /// handlers where that wait matters, such as starting a sound.
    /// </summary>
    /// <remarks>
    /// The action must not touch the Unity API (transforms, components, materials, UI) and must be quick: it
    /// runs inside the workflow. Set a <c>volatile</c> field, write to a lock-free buffer, or call
    /// <see cref="BonsaiDispatcher.RunOnMainThread"/> for the part that needs Unity. <c>Debug.Log</c> is allowed.
    /// </remarks>
    /// <param name="targetNode">The node's display name in the workflow.</param>
    /// <param name="targetType">The node's exact deserialized type.</param>
    /// <param name="action">Receives each emitted value, boxed, and its emission time in <see cref="System.Diagnostics.Stopwatch"/> ticks.</param>
    /// <param name="groupPath">Null for the top level, or a <c>GroupWorkflow</c>'s name (<c>/</c> for nested groups) to search inside it.</param>
    public void InjectImmediateSubscription(string targetNode, Type targetType, Action<object, long> action, string groupPath = null)
    {
        SubscriptionInjections.Add(new SubscriptionInjection { TargetName = targetNode, TargetType = targetType, Action = action, GroupPath = groupPath, Immediate = true });
    }

    /// <summary>
    /// Unity to Bonsai: every value sent on <paramref name="signal"/> is pushed into the workflow's subject
    /// named <paramref name="subjectName"/>, where any <c>SubscribeSubject</c> of that name receives it. Must be
    /// called before the workflow starts, so from <c>Awake</c>.
    /// </summary>
    /// <remarks>
    /// The workflow has to declare the subject: a typed <c>BehaviorSubject</c> or <c>PublishSubject</c> source
    /// (one with a type and no input) of the signal's type, at the top level or in the group named by
    /// <paramref name="groupPath"/>. In Bonsai on its own the subject is silent, so the workflow still runs
    /// there. If the subject is missing, an error is logged at start and the rest of the workflow runs without
    /// the signal; if its type differs from the signal's, the workflow fails to build.
    /// </remarks>
    /// <typeparam name="T">The signal's type, which must be the subject's.</typeparam>
    /// <param name="signal">The signal whose values to push.</param>
    /// <param name="subjectName">The subject's <c>Name</c> in the workflow.</param>
    /// <param name="groupPath">Null if the subject is declared at the top level, or the <c>GroupWorkflow</c> that declares it (<c>/</c> for nested groups).</param>
    public void SendToSubject<T>(UnitySignal<T> signal, string subjectName, string groupPath = null)
    {
        SignalInjections.Add(new SignalInjection { Source = signal, SubjectName = subjectName, GroupPath = groupPath });
    }

    // TODO - we should only be able to inject onto externalizedmapping or propertymapping (or in future e.g. UnitySubject)
    /// <summary>
    /// Feeds a Unity-owned source into the workflow in place of a named externalized property mapping, so
    /// Unity can drive a node's property. Not used by the current scenes.
    /// </summary>
    /// <typeparam name="T">The source's element type.</typeparam>
    /// <param name="source">The source whose values drive the mapped properties.</param>
    /// <param name="targetName">Display name of the <c>ExternalizedMapping</c> node to replace.</param>
    public void InjectInput<T>(Source<T> source, string targetName)
    {
        var externalizedMappings = WorkflowBuilder.Workflow.Where(x => ExpressionBuilder.Unwrap(x.Value).GetType() == typeof(ExternalizedMappingBuilder));
        var targetNode = externalizedMappings.Where(x => ExpressionBuilder.GetElementDisplayName(x.Value) == targetName).FirstOrDefault();

        if (targetNode != null)
        {
            var sourceNodeBuilder = new CombinatorBuilder { Combinator = source };
            InputInjections.Add(new InputInjection { From = sourceNodeBuilder, To = targetNode });
        }
        else
        {
            Debug.LogWarning($"Target node {targetName} for input injection not found.");
        }
    }

   // ----- Specific Injection Methods -----

    /// <summary>
    /// Replaces every node of the named Bonsai type that feeds the named successor (top level, or inside
    /// <paramref name="groupPath"/>) with a
    /// Unity-owned source. Matching is by type name so callers don't need a compile-time reference to it:
    /// <c>KeyDown</c>, for example, emits <c>System.Windows.Forms.Keys</c>, which Unity scripts can't
    /// reference. Must be called before the workflow starts. Logs a warning at start if no node matches.
    /// </summary>
    /// <typeparam name="T">The replacement's element type. Downstream nodes must accept it.</typeparam>
    /// <param name="targetTypeName">Full type name of the combinator to replace, e.g. <c>"Bonsai.Windows.Input.KeyDown"</c>.</param>
    /// <param name="replacement">The source to put in its place. See <see cref="ManualJuiceInput"/> for an example.</param>
    /// <param name="successorName">Display name of the node the target feeds, e.g. <c>"Ctrl-J for Juice"</c>. The workflow has several <c>KeyDown</c> nodes, and this picks which one is replaced.</param>
    /// <param name="groupPath">Null for the top level, or a <c>GroupWorkflow</c>'s name (<c>/</c> for nested groups) to search inside it.</param>
    public void ReplaceSource<T>(string targetTypeName, Source<T> replacement, string successorName, string groupPath = null)
    {
        SourceReplacements.Add(new SourceReplacement { TargetTypeName = targetTypeName, Source = replacement, SuccessorName = successorName, GroupPath = groupPath });
    }

    /// <summary>
    /// Sets a property on every node of the named Bonsai type (top level, or inside <paramref name="groupPath"/>,
    /// which is how two nodes of one type, such as two <c>CsvWriter</c>s, are told apart), e.g. to point a <c>CsvWriter</c>'s
    /// <c>FileName</c> somewhere else. Matched by type name like <see cref="ReplaceSource{T}"/>. Must be
    /// called before the workflow starts. Logs an error at start if the type has no such settable property,
    /// which is also the symptom of the node's DLL being missing (see <see cref="BonsaiDllCheck"/>).
    /// </summary>
    /// <param name="targetTypeName">Full type name of the node, e.g. <c>"Bonsai.IO.CsvWriter"</c>.</param>
    /// <param name="propertyName">The property to set, e.g. <c>"FileName"</c>.</param>
    /// <param name="value">The value. Enum values can be given as their names, e.g. <c>"None"</c>.</param>
    /// <param name="groupPath">Null for the top level, or a <c>GroupWorkflow</c>'s name (<c>/</c> for nested groups) to search inside it.</param>
    public void SetNodeProperty(string targetTypeName, string propertyName, object value, string groupPath = null)
    {
        PropertyOverrides.Add(new PropertyOverride { TargetTypeName = targetTypeName, PropertyName = propertyName, Value = value, GroupPath = groupPath });
    }

    public TimeSpan GetExternalizedTimeSpan(string propertyName)
    {
        // Find the externalized mapping by its display name,
        // e.g. "IncorrectCooldown".
        var mappingNode = WorkflowBuilder.Workflow
            .FirstOrDefault(node =>
                ExpressionBuilder.Unwrap(node.Value) is ExternalizedMappingBuilder &&
                ExpressionBuilder.GetElementDisplayName(node.Value) == propertyName);

        if (mappingNode == null)
        {
            Debug.LogError($"Could not find externalized Bonsai property '{propertyName}'.");
            return TimeSpan.Zero;
        }

        // The mapping is connected directly to the specific Bonsai node it controls.
        var targetNode = mappingNode.Successors.FirstOrDefault()?.Target;

        if (targetNode == null)
        {
            Debug.LogError($"Externalized property '{propertyName}' has no target node.");
            return TimeSpan.Zero;
        }

        object target = ExpressionBuilder.Unwrap(targetNode.Value);

        if (target is CombinatorBuilder combinatorBuilder)
        {
            target = combinatorBuilder.Combinator;
        }

        var property = target.GetType().GetProperty("DueTime");

        if (property == null || property.PropertyType != typeof(TimeSpan))
        {
            Debug.LogError($"Target of '{propertyName}' does not have a TimeSpan DueTime property.");
            return TimeSpan.Zero;
        }

        return (TimeSpan)property.GetValue(target);
    }

    private struct SourceReplacement
    {
        public string TargetTypeName;
        public object Source;
        public string SuccessorName;
        public string GroupPath;
    }

    private struct PropertyOverride
    {
        public string TargetTypeName;
        public string PropertyName;
        public object Value;
        public string GroupPath;
    }

    private struct SubscriptionInjection
    {
        public string TargetName;
        public Type TargetType;
        public Action<object, long> Action;
        public string GroupPath;
        public bool Immediate;   // run the action on Bonsai's thread instead of queueing it for the main thread
    }

    private struct SignalInjection
    {
        public object Source;
        public string SubjectName;
        public string GroupPath;
    }

    private struct InputInjection
    {
        public ExpressionBuilder From;
        public Node<ExpressionBuilder, ExpressionBuilderArgument> To;
    }
}
