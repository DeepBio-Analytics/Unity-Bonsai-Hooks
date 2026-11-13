using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Adds the <c>Bonsai</c> menu to the Unity editor.
/// </summary>
/// <remarks>
/// Adapted from the unmaintained emotional-cities/unity package (https://github.com/emotional-cities/unity)
/// and fixed for Bonsai 2.9; see docs/developer/Bonsai-Internals.md.
/// </remarks>
public class BonsaiEditorMenu : MonoBehaviour
{
    /// <summary>
    /// <c>Bonsai &gt; Create Bonsai workflow asset</c>: asks for a <c>.bonsai</c> file and saves its XML as a
    /// <see cref="BonsaiWorkflowAsset"/> named <c>&lt;file&gt;.bonsai.asset</c> next to it (or in <c>Assets/</c>
    /// if the file is outside the project). If the asset already exists, it offers to overwrite it in place,
    /// which keeps its GUID so scene references survive.
    /// </summary>
    [MenuItem("Bonsai/Create Bonsai workflow asset")]
    static void CreateBonsaiWorkflowAsset()
    {
        string path = EditorUtility.OpenFilePanelWithFilters("Choose workflow file", "", new string[] { "Bonsai Workflow (.bonsai)", "bonsai" });

        // The panel returns an empty string when the user cancels.
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        string fileData = File.ReadAllText(path);
        string workflowName = Path.GetFileName(path);

        // The asset is written next to the source workflow. AssetDatabase only accepts
        // project-relative paths, so a workflow outside the project falls back to Assets/.
        string assetDirectory = "Assets";
        string sourceDirectory = Path.GetDirectoryName(path).Replace('\\', '/');
        string assetsRoot = Application.dataPath;

        if (sourceDirectory.Equals(assetsRoot, System.StringComparison.OrdinalIgnoreCase) ||
            sourceDirectory.StartsWith(assetsRoot + "/", System.StringComparison.OrdinalIgnoreCase))
        {
            assetDirectory = "Assets" + sourceDirectory.Substring(assetsRoot.Length);
        }
        else
        {
            Debug.LogWarning($"{workflowName} is outside the Unity project, so its asset will be created in Assets/ instead.");
        }

        string assetPath = $"{assetDirectory}/{workflowName}.asset";

        if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)))
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "Asset already exists",
                $"{assetPath} already exists. Overwrite it with the current contents of {workflowName}?",
                "Overwrite",
                "Cancel");

            if (!overwrite)
            {
                return;
            }

            // Update in place rather than recreating: CreateAsset on an existing path deletes the
            // old asset, which changes its GUID and breaks scene references to it.
            BonsaiWorkflowAsset existing = AssetDatabase.LoadAssetAtPath<BonsaiWorkflowAsset>(assetPath);
            if (existing != null)
            {
                existing.WorkflowBuilderData = fileData;
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return;
            }
        }

        BonsaiWorkflowAsset asset = ScriptableObject.CreateInstance<BonsaiWorkflowAsset>();
        asset.WorkflowBuilderData = fileData;
        AssetDatabase.CreateAsset(asset, assetPath);
        AssetDatabase.SaveAssets();
    }
}
