using UnityEngine;

/// <summary>
/// A Bonsai workflow as a Unity asset, so it can be assigned to <see cref="BonsaiWorkflow"/>.
/// </summary>
/// <remarks>
/// Every <c>.bonsai</c> file inside <c>Assets/</c> has one of these next to it, <c>&lt;file&gt;.bonsai.asset</c>. The
/// editor rewrites it each time the file is saved in Bonsai, so the asset always matches the file. A workflow kept
/// outside the project gets a copy made with <c>Bonsai &gt; Generate workflow asset</c>, refreshed by running that
/// menu item again.
/// </remarks>
public class BonsaiWorkflowAsset : ScriptableObject
{
    /// <summary>The workflow's XML, exactly as saved by the Bonsai editor.</summary>
    [HideInInspector]
    public string WorkflowBuilderData;
}
