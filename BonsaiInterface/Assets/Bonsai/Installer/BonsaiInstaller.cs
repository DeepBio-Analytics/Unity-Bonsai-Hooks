using UnityEngine;

/// <summary>
/// Settings asset behind <c>Assets/Bonsai/BonsaiInstaller.asset</c>. Selecting the asset shows the
/// custom inspector in <see cref="BonsaiInstallerEditor"/>, which finds the Bonsai install, launches the
/// Bonsai editor and copies the DLLs Unity needs into <c>Assets/Packages</c>.
/// Adapted from the unmaintained emotional-cities/unity package (https://github.com/emotional-cities/unity)
/// and fixed for Bonsai 2.9; see docs/developer/Bonsai-Internals.md.
/// </summary>
public class BonsaiInstaller : ScriptableObject
{
    /// <summary>
    /// Folder of the Bonsai install to use. Leave empty to search the standard install locations; set it
    /// to pin a specific install.
    /// </summary>
    public string BonsaiPath = "";
}
