#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector for <c>Assets/Bonsai/BonsaiInstaller.asset</c>. Shows which Bonsai install is in use and
/// whether <c>Assets/Packages</c> has every DLL in <c>RequiredDlls.txt</c> (see <see cref="BonsaiDllCheck"/>),
/// and has buttons to launch the Bonsai editor, consolidate packages, and download or install a portable copy.
/// </summary>
/// <remarks>
/// <b>Consolidate packages</b> reads the install's <c>Bonsai.config</c> and copies every runtime assembly and
/// x64 native library into <c>Assets/Packages</c>, skipping the editor-only assemblies (Bonsai.Editor,
/// Bonsai.Design and the WinForms/WPF/WebView2 libraries) that Unity can't load. It also refuses the
/// assemblies in <c>KnownProblemAssemblies</c> (BonVision, VR, Audio, Shaders), logging an error for each.
/// Run it after adding packages in Bonsai. It then updates <c>RequiredDlls.txt</c> (<see cref="RequiredDllsGenerator"/>)
/// to the versions just copied.
/// Adapted from the unmaintained emotional-cities/unity package (https://github.com/emotional-cities/unity)
/// and fixed for Bonsai 2.9; see docs/developer/Bonsai-Internals.md.
/// </remarks>
[CustomEditor(typeof(BonsaiInstaller))]
public class BonsaiInstallerEditor : Editor
{
    // GitHub redirects this path to the newest release asset, so no version is pinned here.
    const string BonsaiDownloadUrl = "https://github.com/bonsai-rx/bonsai/releases/latest/download/Bonsai.zip";

    // Assemblies belonging to the Bonsai editor UI. These reference System.Windows.Forms, WPF and
    // WebView2, none of which Unity can resolve, so they are never copied into Assets/Packages.
    static readonly string[] EditorOnlyAssemblies =
    {
        "Bonsai.Editor", "ScintillaNET", "SVG", "SvgNet", "Markdig",
        "YamlDotNet", "WebView2Loader", "ZedGraph", "OpenTK", "OpenTK.GLControl"
    };

    // Prefixes covering assembly families that are editor-only in their entirety.
    static readonly string[] EditorOnlyPrefixes =
    {
        "Microsoft.Web.WebView2", "WeifenLuo.WinFormsUI.Docking"
    };

    // Assemblies (and their sub-namespaces, e.g. Bonsai.Audio.Windows) with known compatibility issues in
    // Unity. They are never copied, and Consolidate packages reports an error for each, so a bad package
    // is caught at install time rather than left in Assets/Packages for someone to delete by hand.
    static readonly string[] KnownProblemAssemblies =
    {
        "Bonsai.BonVision", "Bonsai.VR", "Bonsai.Audio", "Bonsai.Shaders", "Bonsai.Shaders.Renderer"
    };

    // Assets/Packages checked against RequiredDlls.txt. Refreshed on selection and after consolidating,
    // rather than every repaint, since it reads each DLL from disk.
    BonsaiDllCheck.Result dllCheck;

    void OnEnable()
    {
        dllCheck = BonsaiDllCheck.Run();
    }

    /// <summary>True for the Bonsai editor's own assemblies, which Unity can't load and Consolidate never copies.</summary>
    /// <param name="filePath">A DLL's path or file name.</param>
    internal static bool IsEditorOnly(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        if (EditorOnlyPrefixes.Any(x => name.StartsWith(x, StringComparison.OrdinalIgnoreCase))) return true;
        // Bonsai.Design, and everything built on it such as Bonsai.Design.Visualizers.
        if (name.StartsWith("Bonsai.", StringComparison.OrdinalIgnoreCase) &&
            name.IndexOf(".Design", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return EditorOnlyAssemblies.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True for assemblies with known problems in Unity (<c>KnownProblemAssemblies</c>), which Consolidate refuses.</summary>
    /// <param name="filePath">A DLL's path or file name.</param>
    internal static bool IsKnownProblem(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        return KnownProblemAssemblies.Any(x =>
            string.Equals(x, name, StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(x + ".", StringComparison.OrdinalIgnoreCase));
    }

    static string RootPath()
    {
        return Application.dataPath.Replace("/Assets", "");
    }

    static string PortablePath()
    {
        return RootPath() + "/Bonsai";
    }

    static string ArchivePath()
    {
        return RootPath() + "/Bonsai.zip";
    }

    // Install locations in priority order: the path set on the asset, the per-user install written
    // by the Bonsai installer, a machine-wide install, then a portable copy inside the project.
    static IEnumerable<string> CandidatePaths(string pinnedPath)
    {
        if (!string.IsNullOrEmpty(pinnedPath)) yield return pinnedPath;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bonsai");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Bonsai");
        yield return PortablePath();
    }

    static bool IsInstall(string path)
    {
        return !string.IsNullOrEmpty(path)
            && File.Exists(Path.Combine(path, "Bonsai.exe"))
            && File.Exists(Path.Combine(path, "Bonsai.config"));
    }

    string ResolveInstallPath()
    {
        return FindInstall(((BonsaiInstaller)target).BonsaiPath);
    }

    /// <summary>The Bonsai install this project uses, searched as the inspector does; null if there is none.</summary>
    /// <param name="pinnedPath">The path set on the installer asset, or null to search the standard locations only.</param>
    internal static string FindInstall(string pinnedPath)
    {
        return CandidatePaths(pinnedPath).FirstOrDefault(IsInstall);
    }

    /// <summary>As <see cref="FindInstall(string)"/>, with the path pinned on the project's installer asset, if any.</summary>
    internal static string FindInstall()
    {
        string guid = AssetDatabase.FindAssets("t:BonsaiInstaller").FirstOrDefault();
        var installer = guid == null ? null : AssetDatabase.LoadAssetAtPath<BonsaiInstaller>(AssetDatabase.GUIDToAssetPath(guid));
        return FindInstall(installer == null ? null : installer.BonsaiPath);
    }

    static string InstallVersion(string installPath)
    {
        try
        {
            var config = XDocument.Load(Path.Combine(installPath, "Bonsai.config"));
            var package = config.Descendants("Package").FirstOrDefault(x => (string)x.Attribute("id") == "Bonsai");
            return package == null ? "unknown version" : (string)package.Attribute("version");
        }
        catch (Exception ex)
        {
            return "unreadable Bonsai.config: " + ex.Message;
        }
    }

    void DownloadBonsai()
    {
        if (File.Exists(ArchivePath()) &&
            !EditorUtility.DisplayDialog("Download Bonsai", "An archive has already been downloaded. Replace it with the latest release?", "Replace", "Cancel"))
        {
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Bonsai", "Downloading the latest Bonsai release...", 0f);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new WebClient())
            {
                client.DownloadFile(BonsaiDownloadUrl, ArchivePath());
            }
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("Error", "Download failed: " + ex.Message, "OK");
            return;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        EditorUtility.DisplayDialog("Bonsai", "Download complete.", "OK");
    }

    void InstallBonsai()
    {
        if (!File.Exists(ArchivePath()))
        {
            EditorUtility.DisplayDialog("Error", "Bonsai portable archive not found, please download.", "OK");
            return;
        }


        if (IsInstall(PortablePath()))
        {
            EditorUtility.DisplayDialog("Error", "A portable Bonsai install already exists in the project.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Install Bonsai", "The Bonsai archive will now be extracted and installed. Please close the Bonsai editor window after initialization to complete the install process.", "OK", "Cancel"))
        {
            return;
        }

        var extractProcess = new Process();
        extractProcess.StartInfo.FileName = "powershell.exe";
        extractProcess.StartInfo.Arguments =
            "-NoProfile -Command \"Expand-Archive -LiteralPath '" + ArchivePath() + "' -DestinationPath '" + PortablePath() + "' -Force\"";
        extractProcess.StartInfo.UseShellExecute = false;
        extractProcess.StartInfo.CreateNoWindow = true;
        extractProcess.Start();
        extractProcess.WaitForExit();

        // The first launch runs the package bootstrapper; packages are only complete once it closes.
        var firstLaunch = RunBonsai(PortablePath(), null);
        if (firstLaunch != null)
        {
            firstLaunch.WaitForExit();
            ConsolidatePackages(PortablePath());
        }
    }

    void ConsolidatePackages(string installPath)
    {
        // Ensure a packages folder is present
        Directory.CreateDirectory(RootPath() + "/Assets/Packages");

        XDocument config = XDocument.Load(Path.Combine(installPath, "Bonsai.config"));
        var assemblyQuery = from c in config.Descendants("AssemblyLocation")
                            where c.Attribute("location").Value.Contains(".dll")
                            select Path.Combine(installPath, c.Attribute("location").Value);

        var libraryQuery = from c in config.Descendants("LibraryFolder")
                           where c.Attribute("platform").Value.Equals("x64")
                           let folder = Path.Combine(installPath, c.Attribute("path").Value)
                           where Directory.Exists(folder)
                           select Directory.EnumerateFiles(folder);

        var allQueries = assemblyQuery.Concat(libraryQuery.SelectMany(x => x));
        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;
        var flagged = new List<string>();

        foreach (string fromFile in allQueries)
        {
            string fileName = Path.GetFileName(fromFile);

            if (IsEditorOnly(fromFile))
            {
                skipped++;
                continue;
            }

            // Managed assemblies are listed first. A native library of the same name (WebView2 ships
            // both) must not overwrite one, or Unity cannot resolve references against it.
            if (!copied.Add(fileName))
            {
                UnityEngine.Debug.Log($"Skipping {fromFile}, {fileName} was already copied.");
                skipped++;
                continue;
            }

            if (IsKnownProblem(fromFile))
            {
                flagged.Add(fileName);
                UnityEngine.Debug.LogError($"{fileName} has known compatibility issues in Unity and was not copied to Assets/Packages. Remove its package from the Bonsai environment, or check that no workflow Unity runs uses its nodes.");
                skipped++;
                continue;
            }

            string toFile = $"{RootPath()}/Assets/Packages/{fileName}";
            UnityEngine.Debug.Log($"Copying {fromFile} to {toFile}");
            File.Copy(fromFile, toFile, true);
        }

        AssetDatabase.Refresh();

        // Bring the DLL list up to date with the new files. DLLs copied for the first time are only loaded
        // after Unity's next script reload, and their dependencies can't be read until then, so the list is
        // updated again after that reload.
        string listChange = RequiredDllsGenerator.Update(installPath);
        RequiredDllsGenerator.UpdateAfterNextReload();

        dllCheck = BonsaiDllCheck.Run();
        var flaggedNote = flagged.Count == 0
            ? ""
            : $"\n\nERROR: not copied (known Unity compatibility issues). Remove these packages from the Bonsai environment:\n{string.Join("\n", flagged)}";
        EditorUtility.DisplayDialog("Bonsai", $"Package consolidation complete. Copied {copied.Count} files, skipped {skipped}. See debug console for details.\n\n{listChange}\n\n{dllCheck.Summary()}{flaggedNote}", "OK");
    }

    Process RunBonsai(string installPath, EventHandler onExit)
    {
        string executablePath = Path.Combine(installPath, "Bonsai.exe");

        if (!File.Exists(executablePath))
        {
            EditorUtility.DisplayDialog("Error", $"No Bonsai executable could be found at {installPath}.", "OK");
            return null;
        }

        var launchProcess = new Process();
        launchProcess.StartInfo.FileName = executablePath;
        launchProcess.StartInfo.WorkingDirectory = installPath;
        if (onExit != null)
        {
            launchProcess.EnableRaisingEvents = true;
            launchProcess.Exited += onExit;
        }
        launchProcess.Start();
        return launchProcess;
    }

    public override void OnInspectorGUI()
    {
        GUI.skin.label.wordWrap = true;

        serializedObject.Update();
        var pathProperty = serializedObject.FindProperty("BonsaiPath");

        // Show the install actually in use, so the field does not look like it still needs filling
        // in. A detected path is only displayed: nothing is written to the asset unless the user
        // edits the field or picks a folder, since the path is specific to this machine.
        string installPath = ResolveInstallPath();
        string storedPath = pathProperty.stringValue;
        bool detected = string.IsNullOrEmpty(storedPath) && installPath != null;

        EditorGUI.BeginChangeCheck();
        string editedPath = EditorGUILayout.TextField(
            new GUIContent(detected ? "Bonsai Path (detected)" : "Bonsai Path", "Detected automatically. Set this only to pin a specific install; clear it to search the standard locations again."),
            detected ? installPath : storedPath);
        if (EditorGUI.EndChangeCheck())
        {
            pathProperty.stringValue = editedPath;
            serializedObject.ApplyModifiedProperties();
            installPath = ResolveInstallPath();
        }
        if (GUILayout.Button("Locate install..."))
        {
            string selected = EditorUtility.OpenFolderPanel("Choose Bonsai install folder", installPath ?? "", "");
            if (!string.IsNullOrEmpty(selected))
            {
                if (IsInstall(selected))
                {
                    pathProperty.stringValue = selected;
                    installPath = selected;
                }
                else EditorUtility.DisplayDialog("Error", $"{selected} does not contain Bonsai.exe and Bonsai.config.", "OK");
            }
        }
        serializedObject.ApplyModifiedProperties();

        GUILayout.Space(10);

        if (installPath == null)
        {
            EditorGUILayout.HelpBox("No Bonsai install found. Set a path above, or download and install a portable copy into the project.", MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox($"Using Bonsai {InstallVersion(installPath)}\n{installPath}", MessageType.Info);
        }

        EditorGUILayout.HelpBox(dllCheck.Summary(),
            dllCheck.HasErrors ? MessageType.Error : dllCheck.HasWarnings ? MessageType.Warning : MessageType.Info);

        GUILayout.Space(10);

        using (new EditorGUI.DisabledScope(installPath == null))
        {
            // Launch editor
            GUILayout.Label("Launch the Bonsai editor.");
            if (GUILayout.Button("Launch Bonsai"))
            {
                RunBonsai(installPath, (sender, e) => { UnityEngine.Debug.Log("Bonsai editor closed."); });
            }
            GUILayout.Space(10);

            // Consolidate packages
            GUILayout.Label("Consolidate packages. This will copy the Bonsai environment packages to the Unity project, skipping the editor-only assemblies Unity cannot load. This is required to reference Bonsai components from Unity scripts and should be performed whenever new packages are added to the Bonsai environment.");
            if (GUILayout.Button("Consolidate packages"))
            {
                ConsolidatePackages(installPath);
            }
        }

        GUILayout.Space(10);

        // Portable install, only needed when no other install is available
        GUILayout.Label("Only needed when no install was found above: download the latest Bonsai release and install a portable copy inside the project.");
        if (GUILayout.Button("Download latest"))
        {
            DownloadBonsai();
        }
        if (GUILayout.Button("Install portable copy"))
        {
            InstallBonsai();
        }
        GUILayout.Space(10);
    }
}
#endif