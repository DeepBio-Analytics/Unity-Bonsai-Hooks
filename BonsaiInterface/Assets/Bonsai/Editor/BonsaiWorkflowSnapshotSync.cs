using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps a <c>&lt;file&gt;.bonsai.asset</c> next to every <c>.bonsai</c> file in the project: created the first time
/// Unity sees the file and rewritten each time Bonsai saves it.
/// </summary>
/// <remarks>
/// The asset is updated in place, so its GUID and every scene reference to it survive. Rewriting the asset
/// reports a <c>.bonsai.asset</c> path to the postprocessor, not a <c>.bonsai</c> one, so this cannot loop.
/// </remarks>
public class BonsaiWorkflowSnapshotSync : AssetPostprocessor
{
    // Catches up files that changed while the editor was closed or the asset was restored from version control.
    // Once per editor session: later changes arrive through OnPostprocessAllAssets, and domain reloads
    // (every Play) skip it.
    [InitializeOnLoadMethod]
    static void SyncAll()
    {
        if (SessionState.GetBool("Bonsai.SnapshotSyncRan", false))
        {
            return;
        }

        SessionState.SetBool("Bonsai.SnapshotSyncRan", true);
        EditorApplication.delayCall += () =>
        {
            string root = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            foreach (string file in Directory.EnumerateFiles(root, "*.bonsai", SearchOption.AllDirectories))
            {
                string path = "Assets" + file.Replace('\\', '/').Substring(root.Length);
                Write(path, path + ".asset");
            }
        };
    }

    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        foreach (string path in importedAssets)
        {
            if (path.EndsWith(".bonsai", System.StringComparison.OrdinalIgnoreCase) && path.StartsWith("Assets/"))
            {
                Write(path, path + ".asset");
            }
        }
    }

    /// <summary>
    /// Writes the contents of the workflow file at <paramref name="sourcePath"/> into the
    /// <see cref="BonsaiWorkflowAsset"/> at <paramref name="assetPath"/>, creating it if needed. Returns whether
    /// the asset changed.
    /// </summary>
    public static bool Write(string sourcePath, string assetPath)
    {
        string data = ReadShared(sourcePath);
        if (data == null)
        {
            Debug.LogWarning($"Could not read {sourcePath}, so {assetPath} was not updated. Save the workflow again or run Bonsai > Generate workflow asset.");
            return false;
        }

        var existing = AssetDatabase.LoadAssetAtPath<BonsaiWorkflowAsset>(assetPath);
        if (existing != null)
        {
            if (existing.WorkflowBuilderData == data)
            {
                return false;
            }

            existing.WorkflowBuilderData = data;
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssetIfDirty(existing);
            return true;
        }

        var asset = ScriptableObject.CreateInstance<BonsaiWorkflowAsset>();
        asset.name = Path.GetFileNameWithoutExtension(assetPath);
        asset.WorkflowBuilderData = data;
        AssetDatabase.CreateAsset(asset, assetPath);
        return true;
    }

    // Bonsai may still have the file open when Unity notices the change
    static string ReadShared(string path)
    {
        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }
        catch (IOException)
        {
            return null;
        }
    }
}
