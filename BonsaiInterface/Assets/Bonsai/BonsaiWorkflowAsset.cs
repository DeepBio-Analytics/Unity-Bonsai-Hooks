using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Bonsai workflow stored as a Unity asset, so it can be assigned to <see cref="BonsaiWorkflow"/>.
/// Unity can't import <c>.bonsai</c> files directly; create one of these with
/// <c>Bonsai &gt; Create Bonsai workflow asset</c>.
/// </summary>
/// <remarks>
/// The asset is a snapshot of the file's XML, not a link to it. After editing the workflow in Bonsai,
/// run the menu item again and choose Overwrite.
/// </remarks>
public class BonsaiWorkflowAsset : ScriptableObject
{
    /// <summary>The workflow's XML, exactly as saved by the Bonsai editor.</summary>
    [HideInInspector]
    public string WorkflowBuilderData;
}
