using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Adds the <c>Bonsai</c> menu to the Unity editor.
/// </summary>
/// <remarks>
/// A <c>.bonsai.asset</c> next to every <c>.bonsai</c> file inside <c>Assets/</c> is kept up to date by
/// <see cref="BonsaiWorkflowSnapshotSync"/>, so the menu is only needed to force a refresh or for a workflow kept outside the project.
/// Adapted from the unmaintained emotional-cities/unity package (https://github.com/emotional-cities/unity)
/// and fixed for Bonsai 2.9; see docs/developer/Bonsai-Internals.md.
/// </remarks>
public class BonsaiEditorMenu : MonoBehaviour
{
    /// <summary>
    /// <c>Bonsai &gt; Generate workflow asset</c>: asks for a <c>.bonsai</c> file and saves a
    /// <c>&lt;file&gt;.bonsai.asset</c> copy of it in the same folder. A file outside <c>Assets/</c> has no
    /// project folder to save into, so its copy goes in <c>Assets/</c> and is not kept up to date. An existing
    /// asset is updated in place, which keeps its GUID so scene references survive.
    /// </summary>
    [MenuItem("Bonsai/Generate workflow asset")]
    static void GenerateBonsaiWorkflowAsset()
    {
        string path = EditorUtility.OpenFilePanelWithFilters("Choose workflow file", "Assets", new string[] { "Bonsai Workflow (.bonsai)", "bonsai" });
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        string workflowName = Path.GetFileName(path);
        string fullPath = Path.GetFullPath(path).Replace('\\', '/');
        string assetsRoot = Path.GetFullPath(Application.dataPath).Replace('\\', '/');

        string folder = "Assets";
        if (fullPath.StartsWith(assetsRoot + "/", System.StringComparison.OrdinalIgnoreCase))
        {
            string projectPath = "Assets" + fullPath.Substring(assetsRoot.Length);
            folder = Path.GetDirectoryName(projectPath).Replace('\\', '/');
        }
        else
        {
            Debug.LogWarning($"{workflowName} is outside the Unity project, so its asset is a copy: run this menu item again after each change in Bonsai. " +
                             "Moving the .bonsai file into Assets/ makes Unity update the asset automatically.");
        }

        string assetPath = $"{folder}/{workflowName}.asset";
        BonsaiWorkflowSnapshotSync.Write(path, assetPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var saved = AssetDatabase.LoadMainAssetAtPath(assetPath);
        Selection.activeObject = saved;
        EditorGUIUtility.PingObject(saved);
    }
}
