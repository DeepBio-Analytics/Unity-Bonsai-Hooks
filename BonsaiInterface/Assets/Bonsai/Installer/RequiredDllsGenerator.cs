#if UNITY_EDITOR
/*
    Writes Assets/Bonsai/RequiredDlls.txt, the list BonsaiDllCheck checks, so nobody has to keep it by hand.

    Every .bonsai file declares the assemblies its nodes come from in XML namespaces such as
    xmlns:ard="clr-namespace:Bonsai.Arduino;assembly=Bonsai.Arduino". Those are the DLLs used directly. Their
    dependencies are read from the copies in Assets/Packages, which Unity loads into the editor, through
    Assembly.GetReferencedAssemblies, and followed until they leave the Bonsai packages. An assembly counts as a
    Bonsai package if it is in Assets/Packages or the Bonsai install's Bonsai.config lists it; everything else
    (mscorlib, System.Xml, System.Windows.Forms) is taken to come with .NET or Unity and is only noted.

    This lives in the Installer assembly, which compiles without any Bonsai DLL, so it still works in a fresh
    clone where the bridge itself can't compile yet. Its limits there are handled so the file never gets worse:

      - A DLL that isn't loaded (not consolidated yet) has dependencies nobody can read. The ones the previous
        list gave it are kept until it can be read.
      - Automatic updates keep the versions already in the list, which pin what the team uses; otherwise a
        computer with older DLLs would rewrite them and silence the version warning. Consolidate packages and
        the menu item take the versions in Assets/Packages, and say which changed.
      - The section starting "# Added by hand" is copied unchanged, for DLLs the project's own scripts need.

    It runs when a workflow is imported, moved or deleted, after Consolidate packages (and again after the
    script reload that loads newly copied DLLs), and from Bonsai > Update RequiredDlls.txt.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Regenerates <c>Assets/Bonsai/RequiredDlls.txt</c> from the project's workflows and the DLLs in
/// <c>Assets/Packages</c>. See the comment at the top of the file for the rules.
/// </summary>
[InitializeOnLoad]
public static class RequiredDllsGenerator
{
    /// <summary>The line that starts the hand-kept section; it and everything after it is never rewritten.</summary>
    public const string HandSectionHeader = "# Added by hand";

    const string PackagesPath = "Assets/Packages";
    const string ReloadKey = "Bonsai.UpdateRequiredDllsAfterReload";
    const string BridgeRequirer = "the Unity bridge";
    const int WorkflowsShown = 4;

    static readonly Regex ClrNamespace = new Regex(@"^\s*clr-namespace:[^;]*;\s*assembly=([^;,\s]+)", RegexOptions.IgnoreCase);
    static bool scheduled;

    class Entry
    {
        public string Name;
        public string File;
        public string Version;
        public bool Direct;
        public readonly SortedSet<string> NeededBy = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    static RequiredDllsGenerator()
    {
        if (SessionState.GetBool(ReloadKey, false))
        {
            SessionState.EraseBool(ReloadKey);
            EditorApplication.delayCall += () => Update(null, takeLocalVersions: true);
        }
    }

    /// <summary>Updates the list once more after Unity's next script reload, when newly copied DLLs are loaded.</summary>
    public static void UpdateAfterNextReload()
    {
        SessionState.SetBool(ReloadKey, true);
    }

    /// <summary>Updates the list on the next editor tick, once however many workflows were imported together.</summary>
    internal static void Schedule()
    {
        if (scheduled) return;
        scheduled = true;
        EditorApplication.delayCall += () =>
        {
            scheduled = false;
            Update(null, takeLocalVersions: false);
        };
    }

    [MenuItem("Bonsai/Update RequiredDlls.txt")]
    static void UpdateFromMenu()
    {
        EditorUtility.DisplayDialog("RequiredDlls.txt", Update(null, takeLocalVersions: true), "OK");
    }

    /// <summary>For <c>-batchmode -executeMethod RequiredDllsGenerator.UpdateFromCommandLine</c>.</summary>
    public static void UpdateFromCommandLine()
    {
        Update(null, takeLocalVersions: true);
    }

    /// <summary>
    /// Rewrites the list if anything in it would change, and logs what did. Taking the versions in
    /// <c>Assets/Packages</c> is what Consolidate packages does; pass <paramref name="takeLocalVersions"/> false
    /// to keep the versions already listed.
    /// </summary>
    /// <param name="installPath">The Bonsai install, or null to look for it as the installer inspector does.</param>
    /// <param name="takeLocalVersions">Use the versions of the DLLs in <c>Assets/Packages</c> for entries already listed.</param>
    /// <returns>One sentence saying what changed, for a dialog.</returns>
    public static string Update(string installPath = null, bool takeLocalVersions = true)
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        string manifestFile = Path.Combine(root, BonsaiDllCheck.ManifestPath);
        string previousText = File.Exists(manifestFile) ? File.ReadAllText(manifestFile) : "";

        var previous = ParsePrevious(previousText, out string handSection);
        var notes = new List<string>();
        var entries = Collect(root, installPath ?? BonsaiInstallerEditor.FindInstall(), previous, takeLocalVersions, notes, out var framework, out bool incomplete);
        string text = Render(entries, notes, framework, handSection);

        if (Normalize(text) == Normalize(previousText))
        {
            return $"{BonsaiDllCheck.ManifestPath} is up to date.";
        }

        // With DLLs that couldn't be read (a fresh clone, before Consolidate packages), the comments come out
        // shorter than they really are. Rewriting the file for that alone would only make a pointless change.
        string change = DescribeChange(previous, entries, out bool dllsChanged);
        if (incomplete && !dllsChanged)
        {
            return $"{BonsaiDllCheck.ManifestPath} is up to date as far as can be told: some DLLs aren't in {PackagesPath} yet, so their dependencies can't be read. Consolidate packages, then update again.";
        }

        File.WriteAllText(manifestFile, text);
        AssetDatabase.ImportAsset(BonsaiDllCheck.ManifestPath);
        Debug.Log(change);
        BonsaiDllCheck.Log(BonsaiDllCheck.Run(), asError: false);
        return change;
    }

    static Dictionary<string, Entry> Collect(string root, string installPath, Dictionary<string, Entry> previous,
        bool takeLocalVersions, List<string> notes, out SortedDictionary<string, SortedSet<string>> framework, out bool incomplete)
    {
        var packageFiles = PackageFiles(Path.Combine(root, PackagesPath));
        var installFiles = InstallFiles(installPath);
        var loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        // Unity's analyzer prefers CurrentAssemblies.GetLoadedAssemblies, which only recent Unity 6 releases
        // have. On Mono, which Bonsai needs anyway, a script reload replaces the whole domain, so nothing
        // listed here has been unloaded.
#pragma warning disable UAC0005
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic))
#pragma warning restore UAC0005
        {
            string name = assembly.GetName().Name;
            if (!loaded.ContainsKey(name)) loaded[name] = assembly;
        }

        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        var notPackages = new SortedDictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var unreadable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var carriedOver = new HashSet<(string, string)>();
        var queue = new Queue<string>();

        void Require(string name, string neededBy, bool direct, bool knownPackage)
        {
            if (BonsaiInstallerEditor.IsEditorOnly(name + ".dll") || BonsaiInstallerEditor.IsKnownProblem(name + ".dll"))
            {
                // Consolidate never copies these, so listing one would be an error nobody can fix. Only a
                // workflow using one directly is worth saying anything about.
                if (direct && excluded.Add($"{name}|{neededBy}"))
                {
                    string note = $"{neededBy} uses {name}, which can't run inside Unity, so it is not listed. Its nodes will do nothing.";
                    notes.Add(note);
                    Debug.LogWarning(note);
                }
                return;
            }

            bool isPackage = knownPackage || packageFiles.ContainsKey(name) || installFiles.ContainsKey(name);

            // A workflow's own assembly is listed even when nothing is known about it: if it isn't part of .NET
            // or Unity, it has to be a package, and the check should report it missing
            if (!isPackage && (!direct || loaded.ContainsKey(name)))
            {
                if (!notPackages.TryGetValue(name, out var by)) notPackages[name] = by = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                by.Add(neededBy);
                return;
            }

            if (!entries.TryGetValue(name, out var entry))
            {
                entries[name] = entry = new Entry { Name = name };
                queue.Enqueue(name);
            }

            entry.NeededBy.Add(neededBy);
            entry.Direct |= direct;
        }

        Require("Bonsai.Core", BridgeRequirer, direct: true, knownPackage: true);
        foreach (var workflow in Workflows(root))
        {
            try
            {
                foreach (string assembly in DeclaredAssemblies(workflow.Value))
                {
                    Require(assembly, workflow.Key, direct: true, knownPackage: false);
                }
            }
            catch (XmlException ex)
            {
                notes.Add($"{workflow.Key} could not be read ({ex.Message}), so the DLLs it uses are not listed.");
            }
        }

        while (true)
        {
            while (queue.Count > 0)
            {
                string name = queue.Dequeue();
                if (!loaded.TryGetValue(name, out var assembly))
                {
                    unreadable.Add(name);
                    continue;
                }

                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    Require(reference.Name, name, direct: false, knownPackage: false);
                }
            }

            // What the previous list gave an unreadable DLL stands until the DLL can be read. Each pair is tried
            // once, since Require may still refuse one (an editor-only DLL listed by hand above the hand section).
            var carried = previous.Values
                .SelectMany(old => old.NeededBy.Where(unreadable.Contains).Select(by => (old.Name, by)))
                .Where(pair => carriedOver.Add(pair))
                .ToList();
            if (carried.Count == 0) break;

            foreach (var (name, by) in carried)
            {
                Require(name, by, direct: false, knownPackage: true);
            }
        }

        incomplete = unreadable.Count > 0;
        if (incomplete)
        {
            notes.Add($"Not loaded, so their dependencies are as last listed: {string.Join(", ", unreadable.OrderBy(x => x))}. Consolidate packages to update them.");
        }

        foreach (var entry in entries.Values)
        {
            previous.TryGetValue(entry.Name, out var old);
            string file = packageFiles.TryGetValue(entry.Name, out string local) ? local
                : installFiles.TryGetValue(entry.Name, out string installed) && File.Exists(installed) ? installed
                : null;

            entry.File = file != null ? Path.GetFileName(file) : old?.File ?? entry.Name + ".dll";
            string version = file != null ? ReadVersion(file) : null;
            entry.Version = old != null && (!takeLocalVersions || version == null) ? old.Version : version ?? "-";
        }

        framework = notPackages;
        return entries;
    }

    // Workflow file name → XML: every .bonsai file in Assets, and any snapshot (.bonsai.asset) whose .bonsai
    // file isn't in the project
    static SortedDictionary<string, string> Workflows(string root)
    {
        var result = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var paths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/")).ToList();
        var files = new HashSet<string>(paths.Where(p => p.EndsWith(".bonsai", StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);

        // By type name, so it also works when the bridge's scripts don't compile
        var snapshots = AssetDatabase.FindAssets("t:BonsaiWorkflowAsset", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Concat(paths.Where(p => p.EndsWith(".bonsai.asset", StringComparison.OrdinalIgnoreCase)))
            .Where(p => !files.Contains(p))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (string path in files)
        {
            try
            {
                result[Path.GetFileName(path)] = File.ReadAllText(Path.Combine(root, path));
            }
            catch (IOException ex)
            {
                Debug.LogWarning($"Could not read {path} for {BonsaiDllCheck.ManifestPath}: {ex.Message}");
            }
        }

        foreach (string path in snapshots)
        {
            if (path.EndsWith(".bonsai.asset", StringComparison.OrdinalIgnoreCase) && files.Contains(path.Substring(0, path.Length - ".asset".Length)))
            {
                continue;   // a stale copy of a file already read
            }

            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            string xml = asset == null ? null : new SerializedObject(asset).FindProperty("WorkflowBuilderData")?.stringValue;
            if (!string.IsNullOrEmpty(xml))
            {
                result[Path.GetFileName(path)] = xml;
            }
        }

        return result;
    }

    // Assembly names from every clr-namespace declaration in the workflow, plus Bonsai.Core, whose types the
    // default namespace (WorkflowBuilder, Expression) refers to
    static IEnumerable<string> DeclaredAssemblies(string xml)
    {
        var document = XDocument.Parse(xml);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Bonsai.Core" };
        foreach (var attribute in document.Descendants().Attributes().Where(a => a.IsNamespaceDeclaration))
        {
            var match = ClrNamespace.Match(attribute.Value);
            if (match.Success) names.Add(match.Groups[1].Value);
        }

        return names;
    }

    // Assembly name → path for the managed DLLs in Assets/Packages; native libraries are skipped
    static Dictionary<string, string> PackageFiles(string folder)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return result;

        foreach (string file in Directory.EnumerateFiles(folder, "*.dll"))
        {
            try
            {
                result[AssemblyName.GetAssemblyName(file).Name] = file;
            }
            catch (Exception)
            {
                // BadImageFormatException for native libraries, which have no assembly name
            }
        }

        return result;
    }

    // Assembly name → path for the DLLs the install's Bonsai.config lists, as Consolidate packages reads them
    static Dictionary<string, string> InstallFiles(string installPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string config = installPath == null ? null : Path.Combine(installPath, "Bonsai.config");
        if (config == null || !File.Exists(config)) return result;

        try
        {
            foreach (var location in XDocument.Load(config).Descendants("AssemblyLocation"))
            {
                string name = (string)location.Attribute("assemblyName");
                string path = (string)location.Attribute("location");
                if (name != null && path != null && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    result[name] = Path.Combine(installPath, path);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Could not read {config} for {BonsaiDllCheck.ManifestPath}: {ex.Message}");
        }

        return result;
    }

    static string ReadVersion(string file)
    {
        try
        {
            return AssemblyName.GetAssemblyName(file).Version.ToString(3);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // The entries above the hand-kept section, by assembly name, and that section's text (with its header)
    static Dictionary<string, Entry> ParsePrevious(string text, out string handSection)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        int handStart = Array.FindIndex(lines, line => line.StartsWith(HandSectionHeader, StringComparison.OrdinalIgnoreCase));
        handSection = handStart < 0 ? null : string.Join(Environment.NewLine, lines.Skip(handStart)).TrimEnd();

        foreach (string raw in handStart < 0 ? lines : lines.Take(handStart))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            string[] fields = line.Split((char[])null, 3, StringSplitOptions.RemoveEmptyEntries);
            var entry = new Entry
            {
                Name = Path.GetFileNameWithoutExtension(fields[0]),
                File = fields[0],
                Version = fields.Length > 1 ? fields[1] : "-",
            };

            if (fields.Length > 2)
            {
                foreach (string by in fields[2].Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                {
                    entry.NeededBy.Add(by.Trim());
                }
            }

            entries[entry.Name] = entry;
        }

        return entries;
    }

    static string Render(Dictionary<string, Entry> entries, List<string> notes, SortedDictionary<string, SortedSet<string>> framework, string handSection)
    {
        var direct = entries.Values.Where(e => e.Direct).OrderBy(e => e.File, StringComparer.OrdinalIgnoreCase).ToList();
        var dependencies = entries.Values.Where(e => !e.Direct).OrderBy(e => e.File, StringComparer.OrdinalIgnoreCase).ToList();
        int fileWidth = Math.Max(36, entries.Values.Select(e => e.File.Length + 1).DefaultIfEmpty(0).Max());
        int versionWidth = Math.Max(10, entries.Values.Select(e => e.Version.Length + 1).DefaultIfEmpty(0).Max());

        var text = new StringBuilder();
        void Line(string line = "") => text.Append(line).Append(Environment.NewLine);
        string Row(Entry e) => $"{e.File.PadRight(fileWidth)} {e.Version.PadRight(versionWidth)} {NeededBy(e)}".TrimEnd();

        Line("# DLLs that must be in Assets/Packages for the Bonsai workflows in this project. Checked by");
        Line("# Installer/BonsaiDllCheck.cs.");
        Line("#");
        Line("# GENERATED by Installer/RequiredDllsGenerator.cs from the assemblies each workflow declares and what");
        Line("# those reference in turn. It is rewritten when a workflow changes, after \"Consolidate packages\", and");
        Line("# from Bonsai > Update RequiredDlls.txt. Edit only the section at the end, which is kept as written.");
        Line("#");
        Line("# None of these are in git (Assets/Packages is gitignored), so a fresh clone gets them from a Bonsai");
        Line("# install: install the packages below in Bonsai, then select Assets/Bonsai/BonsaiInstaller.asset and");
        Line("# click \"Consolidate packages\".");
        Line("#");
        Line("# A missing DLL does not stop the workflow from loading. Bonsai swaps every node from that");
        Line("# assembly for a proxy with the same type name and no properties, so the workflow starts but those");
        Line("# nodes do nothing. The only symptom is an error like");
        Line("# \"Bonsai.IO.CsvWriter has no settable property FileName\".");
        Line("#");
        Line("# Versions are assembly versions (major.minor.build); a DLL at another version is a warning, not an");
        Line("# error. Updates after a workflow changes keep the versions listed here. Consolidate packages and the");
        Line("# menu item take the versions in Assets/Packages.");
        Line("#");
        Line($"# {"<dll>".PadRight(fileWidth - 2)} {"<version>".PadRight(versionWidth)} <needed by>");
        Line();
        Line("# Used directly by the workflows");
        direct.ForEach(e => Line(Row(e)));

        if (dependencies.Count > 0)
        {
            Line();
            Line("# Dependencies of the above");
            dependencies.ForEach(e => Line(Row(e)));
        }

        if (notes.Count > 0 || framework.Count > 0)
        {
            Line();
            foreach (string note in notes)
            {
                Line($"# Note: {note}");
            }

            if (framework.Count > 0)
            {
                Line("# Not checked, taken to come with .NET or Unity:");
                foreach (string wrapped in Wrap(framework.Keys, 96))
                {
                    Line($"#   {wrapped}");
                }
            }
        }

        Line();
        if (handSection != null)
        {
            Line(handSection);
        }
        else
        {
            Line($"{HandSectionHeader} (kept when this file is regenerated): DLLs your own scripts need, one per");
            Line("# line as above. Everything from the line above to the end of the file is left as written.");
        }

        return text.ToString();
    }

    // A DLL used directly lists only its workflows, cut short when there are many. A dependency lists every
    // assembly that needs it, since those names are read back to carry dependencies over (see Collect).
    static string NeededBy(Entry entry)
    {
        if (!entry.Direct)
        {
            return string.Join(", ", entry.NeededBy);
        }

        var workflows = entry.NeededBy.Where(IsWorkflow).ToList();
        var shown = workflows.Take(WorkflowsShown).ToList();
        if (workflows.Count > WorkflowsShown) shown.Add($"{workflows.Count - WorkflowsShown} more workflows");
        if (entry.NeededBy.Contains(BridgeRequirer)) shown.Insert(0, BridgeRequirer);
        return string.Join(", ", shown);
    }

    static bool IsWorkflow(string neededBy)
    {
        return neededBy.EndsWith(".bonsai", StringComparison.OrdinalIgnoreCase)
            || neededBy.EndsWith(".bonsai.asset", StringComparison.OrdinalIgnoreCase)
            || neededBy.EndsWith(" more workflows", StringComparison.OrdinalIgnoreCase);
    }

    static IEnumerable<string> Wrap(IEnumerable<string> words, int width)
    {
        var line = new StringBuilder();
        foreach (string word in words)
        {
            if (line.Length > 0 && line.Length + word.Length + 2 > width)
            {
                yield return line.ToString().TrimEnd(',', ' ');
                line.Clear();
            }
            line.Append(word).Append(", ");
        }
        if (line.Length > 0) yield return line.ToString().TrimEnd(',', ' ');
    }

    static string DescribeChange(Dictionary<string, Entry> previous, Dictionary<string, Entry> current, out bool dllsChanged)
    {
        var added = current.Values.Where(e => !previous.ContainsKey(e.Name)).Select(e => e.File).OrderBy(x => x).ToList();
        var removed = previous.Values.Where(e => !current.ContainsKey(e.Name)).Select(e => e.File).OrderBy(x => x).ToList();
        var versions = current.Values
            .Where(e => previous.TryGetValue(e.Name, out var old) && old.Version != e.Version)
            .Select(e => $"{e.File} {previous[e.Name].Version} -> {e.Version}")
            .OrderBy(x => x).ToList();

        var parts = new List<string>();
        if (added.Count > 0) parts.Add("added " + string.Join(", ", added));
        if (removed.Count > 0) parts.Add("removed " + string.Join(", ", removed));
        if (versions.Count > 0) parts.Add("versions changed: " + string.Join(", ", versions));
        dllsChanged = parts.Count > 0;
        return parts.Count == 0
            ? $"{BonsaiDllCheck.ManifestPath} updated (comments and 'needed by' only; the DLLs and versions are the same)."
            : $"{BonsaiDllCheck.ManifestPath} updated: {string.Join("; ", parts)}.";
    }

    static string Normalize(string text)
    {
        return text.Replace("\r\n", "\n").TrimEnd();
    }
}

/// <summary>Updates <c>RequiredDlls.txt</c> whenever a workflow is imported, moved or deleted.</summary>
class RequiredDllsPostprocessor : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(IsWorkflow))
        {
            RequiredDllsGenerator.Schedule();
        }
    }

    static bool IsWorkflow(string path)
    {
        return path.StartsWith("Assets/")
            && (path.EndsWith(".bonsai", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bonsai.asset", StringComparison.OrdinalIgnoreCase));
    }
}
#endif
