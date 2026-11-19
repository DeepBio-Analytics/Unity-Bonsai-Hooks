#if UNITY_EDITOR
/*
    Checks Assets/Packages against the list in Assets/Bonsai/RequiredDlls.txt.

    Bonsai DLLs are gitignored, so a checkout can be missing some. Bonsai does not fail when
    that happens: nodes from a missing assembly load as proxies with no properties and do nothing,
    and the only symptom is an unrelated-looking error such as
    "Bonsai.IO.CsvWriter has no settable property FileName". This check names the missing DLLs
    instead: as a warning when scripts reload, as an error when entering Play mode, and by failing
    player builds. The BonsaiInstaller inspector shows the same result.

    The list itself is written by RequiredDllsGenerator from the project's workflows.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Compares <c>Assets/Packages</c> with the DLL list in <c>Assets/Bonsai/RequiredDlls.txt</c>. Runs when
/// scripts reload (warning), when entering Play mode (error), and before player builds (fails the build).
/// </summary>
[InitializeOnLoad]
public static class BonsaiDllCheck
{
    /// <summary>Project-relative path of the list of required DLLs.</summary>
    public const string ManifestPath = "Assets/Bonsai/RequiredDlls.txt";
    const string PackagesPath = "Assets/Packages";
    const string FixHint = "Select Assets/Bonsai/BonsaiInstaller.asset and click 'Consolidate packages'.";

    /// <summary>The outcome of one <see cref="Run"/>.</summary>
    public class Result
    {
        /// <summary>How many DLLs the list requires.</summary>
        public int RequiredCount;
        /// <summary>Set if the list itself couldn't be read; the other fields are then empty.</summary>
        public string ManifestError;
        /// <summary>Required DLLs not found in <c>Assets/Packages</c>.</summary>
        public readonly List<string> Missing = new List<string>();
        /// <summary>DLLs present at a different version from the list's, one description each.</summary>
        public readonly List<string> WrongVersion = new List<string>();

        /// <summary>True if the list is unreadable or any DLL is missing.</summary>
        public bool HasErrors => ManifestError != null || Missing.Count > 0;
        /// <summary>True if any DLL is at a different version from the list's.</summary>
        public bool HasWarnings => WrongVersion.Count > 0;

        /// <summary>A human-readable description of the result, including how to fix any problem.</summary>
        public string Summary()
        {
            if (ManifestError != null) return ManifestError;

            var lines = new List<string>();
            if (Missing.Count > 0)
            {
                lines.Add($"Missing Bonsai DLLs in {PackagesPath}: {string.Join(", ", Missing)}. " +
                          $"Workflow nodes from these assemblies will load as empty proxies and do nothing. {FixHint}");
            }
            if (WrongVersion.Count > 0)
            {
                lines.Add($"Bonsai DLL versions differ from {ManifestPath}: {string.Join("; ", WrongVersion)}. " +
                          "Update the Bonsai packages and consolidate, or update the list if the new versions are intended.");
            }
            if (lines.Count == 0) lines.Add($"All {RequiredCount} required Bonsai DLLs are present in {PackagesPath}.");
            return string.Join("\n", lines);
        }
    }

    static BonsaiDllCheck()
    {
        // Wait for the editor to finish loading before touching the asset folders.
        EditorApplication.delayCall += () => Log(Run(), asError: false);
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) Log(Run(), asError: true);
        };
    }

    /// <summary>
    /// Checks every DLL in the list: that it exists in <c>Assets/Packages</c>, and that its assembly version
    /// matches the list's major.minor.build. Reads version metadata without loading the assemblies.
    /// </summary>
    public static Result Run()
    {
        var result = new Result();
        string root = Path.GetDirectoryName(Application.dataPath);
        string manifestFile = Path.Combine(root, ManifestPath);

        if (!File.Exists(manifestFile))
        {
            result.ManifestError = $"{ManifestPath} not found, so the Bonsai DLLs could not be checked.";
            return result;
        }

        foreach (string rawLine in File.ReadAllLines(manifestFile))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            // <dll> <version> <needed by...>; anything after the version is a comment.
            string[] fields = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            string dllName = fields[0];
            result.RequiredCount++;

            string dllFile = Path.Combine(root, PackagesPath, dllName);
            if (!File.Exists(dllFile))
            {
                result.Missing.Add(dllName);
                continue;
            }

            if (fields.Length < 2 || !Version.TryParse(fields[1], out Version expected)) continue;

            try
            {
                // Reads the version from the file's metadata without loading the assembly.
                Version actual = AssemblyName.GetAssemblyName(dllFile).Version;
                if (actual.Major != expected.Major || actual.Minor != expected.Minor || actual.Build != Math.Max(expected.Build, 0))
                {
                    result.WrongVersion.Add($"{dllName} is {actual.ToString(3)}, expected {expected}");
                }
            }
            catch (Exception ex)
            {
                result.WrongVersion.Add($"{dllName} could not be read ({ex.Message})");
            }
        }

        return result;
    }

    /// <summary>Logs a result's problems, if any, as warnings or (with <paramref name="asError"/>) errors.</summary>
    internal static void Log(Result result, bool asError)
    {
        if (result.HasErrors)
        {
            if (asError) Debug.LogError(result.Summary());
            else Debug.LogWarning(result.Summary());
        }
        else if (result.HasWarnings)
        {
            Debug.LogWarning(result.Summary());
        }
    }
}

/// <summary>
/// Fails player builds when a required Bonsai DLL is missing. A player built without them would have the
/// same silent failure as the editor.
/// </summary>
public class BonsaiDllBuildCheck : IPreprocessBuildWithReport
{
    /// <inheritdoc/>
    public int callbackOrder => 0;

    /// <summary>Runs <see cref="BonsaiDllCheck.Run"/> and throws <see cref="BuildFailedException"/> on errors.</summary>
    /// <param name="report">Unity's build report (unused).</param>
    public void OnPreprocessBuild(BuildReport report)
    {
        BonsaiDllCheck.Result result = BonsaiDllCheck.Run();
        if (result.HasErrors) throw new BuildFailedException(result.Summary());
        if (result.HasWarnings) Debug.LogWarning(result.Summary());
    }
}
#endif
