# Troubleshooting

[README](../README.md) › Troubleshooting

Find the message or symptom, then follow the steps in order. Messages appear in Unity's **Console** (**Window › General › Console**). Yellow lines are warnings, red lines are errors. Click a line to see all of it at the bottom of the Console.

Long technical text under every Console message (lines starting `UnityEngine.Debug...` or `at ...`) is normal. It shows where the message came from; read the first line.

| Message or symptom | Go to |
|---|---|
| Unity asks to **Enter Safe Mode** when opening the project | [Missing DLLs](#missing-bonsai-dlls) |
| `Missing Bonsai DLLs in Assets/Packages: ...` | [Missing DLLs](#missing-bonsai-dlls) |
| `Bonsai DLL versions differ from ...` | [Different DLL versions](#different-dll-versions) |
| Red errors about `Bonsai` or `System.Reactive` "could not be found" | [Missing DLLs](#missing-bonsai-dlls) |
| BonsaiInstaller shows **No Bonsai install found** or Bonsai Path is empty | [Bonsai not found](#bonsai-not-found) |
| There is no **Bonsai** menu at the top of Unity | [No Bonsai menu](#no-bonsai-menu) |
| Nothing happens when I press Play | [Nothing happens](#nothing-happens-when-i-press-play) |
| `Target node X for subscription injection not found.` | [Name not found](#name-not-found) |
| `Target node X for input injection not found.` | [Name not found](#name-not-found) |
| `... has no settable property ...` | [Missing DLLs](#missing-bonsai-dlls) |
| `No ... node found in the workflow ...` | [Name not found](#name-not-found) |
| I changed the workflow but Unity behaves the same | [Changes do nothing](#my-workflow-changes-do-nothing) |
| `Access to the port 'COMx' is denied` or `The port 'COMx' does not exist` | [Serial port errors](#serial-port-errors) |
| Red errors after pasting a script | [Red errors after pasting](#red-errors-after-pasting-a-script) |
| `The associated script can not be loaded` on a component | [Script can't be loaded](#the-associated-script-can-not-be-loaded) |
| A node works in Bonsai but does nothing in Unity | [Works in Bonsai, not in Unity](#works-in-bonsai-not-in-unity) |
| Part of the workflow stops after an error | [Workflow stopped after an error](#workflow-stopped-after-an-error) |
| `Dictionary.TryInsert` errors from `Bonsai.Arduino` | [Gotchas › Arduino PinMode race](Gotchas.md#the-arduino-pinmode-race) |
| `No valid build context was provided` | [Gotchas › Build context](Gotchas.md#build-context-and-double-wrapping) |

---

## Missing Bonsai DLLs

Unity doesn't have a copy of one or more of Bonsai's files. Nodes from a missing file still load, but they **silently do nothing**, so fix this before anything else.

On a fresh project, `Bonsai.Core.dll` is missing, so Unity reports compile errors and offers **Enter Safe Mode** when it opens. Click **Ignore** (or **Exit Safe Mode** if it is already in Safe Mode), then follow the steps below.

1. Make sure the Bonsai packages your workflow uses are installed in **Bonsai** (Start menu › Bonsai › **Manage packages** › **Installed** tab). Install any that are missing, then close Bonsai.
2. In Unity, select **Assets › Bonsai › BonsaiInstaller** in the Project window.
3. Click **Consolidate packages** and wait for **Package consolidation complete**.
4. Read the box in the Inspector. If it still lists missing DLLs, the package that contains them isn't installed in the Bonsai shown under **Bonsai Path**. Repeat step 1 for that install.

The list of files Unity checks is **Assets › Bonsai › RequiredDlls.txt**. If the message names a file your workflow doesn't use any more, delete that line from the list.

## Different DLL versions

The files in `Assets/Packages` are a different version from the one in `RequiredDlls.txt`. Usually Bonsai was updated on this computer, or the list is out of date. It is a warning: things may still work.

- If this computer's Bonsai should match the others, install the version named in the message (Bonsai: **Manage packages** › **Installed**, pick the version), then consolidate again.
- If everyone has moved to the new version, open `RequiredDlls.txt` in Notepad and change the version on that line.

## Bonsai not found

The **Bonsai Path** box is filled in from the first Bonsai it finds: one typed in the box, then `%LOCALAPPDATA%\Bonsai`, then `C:\Program Files\Bonsai`, then a copy inside the project.

1. If Bonsai isn't installed, install it ([Getting started, step 2](Getting-Started.md#2-install-bonsai)), then click away from BonsaiInstaller and back.
2. If it is installed somewhere else, click **Locate install...** and choose the folder that contains `Bonsai.exe`.
3. Without internet access to install normally, **Download latest** and **Install portable copy** put a copy inside the project instead.

## No Bonsai menu

The menu comes from a script in **Assets › Bonsai › Editor**. It is missing if that folder is missing or Unity can't compile scripts.

1. Check the Console for **red errors**. Unity adds no menus while any script has errors. Fix those first (often [missing DLLs](#missing-bonsai-dlls)).
2. Check **Assets › Bonsai › Editor › BonsaiEditorMenu** exists. If not, copy the whole `Assets/Bonsai` folder from this project again.

## Nothing happens when I press Play

Work down this list:

1. **Console errors?** Read the first red line and look it up in the table at the top of this page.
2. **Auto Start ticked?** Select the object with **Bonsai Workflow** and check **Auto Start** is ticked. Without it the workflow never runs.
3. **Asset assigned?** The **Bonsai Workflow Asset** field must show your workflow, not **None**.
4. **Script attached?** The listening script (for example **Show Bonsai Value**) must be on the **same object** as Bonsai Workflow, and be ticked (enabled).
5. **Right name?** The subject name in the script's settings must match the **Name** in Bonsai exactly. A mismatch prints [`Target node ... not found`](#name-not-found).
6. **Does the workflow produce anything?** Open it in Bonsai, press **F5**, and double-click the subject to watch it. If nothing appears there either, the problem is in the workflow, not Unity. Stop it (Shift+F5) before going back to Unity.
7. **Running the file you edited?** See [Changes do nothing](#my-workflow-changes-do-nothing).
8. **Game view visible?** On-screen text from the recipes appears in the **Game** tab, not the Scene tab.

## Name not found

Unity couldn't find a node with that name **and** that kind on the main level of the workflow.

1. Open the workflow in Bonsai. Find the node and check its **Name** (or, for an externalized property, its display name) letter by letter, capitals included. No extra spaces.
2. Check the node is on the **main level**: not inside a group or other box you double-click to open. If it is, move it out, or connect what you need to a new PublishSubject on the main level.
3. Check the **kind** of node. Recipes look for a **PublishSubject**. A **BehaviorSubject** with the same name won't be found unless the script says `BehaviorSubject` instead ([Hook API › Which type to pass](Hook-API.md#which-type-to-pass)).
4. Save, click back into Unity, and press Play again ([Getting started, step 12](Getting-Started.md#12-when-you-change-the-workflow)).

## My workflow changes do nothing

Unity regenerates `x.bonsai.asset` every time `x.bonsai` inside **Assets** is saved. If a change still does nothing:

1. **Was Unity in Play mode?** The workflow is read when Play starts. Stop and press Play again.
2. **Did Unity notice?** It checks for changed files when its window becomes active. Click into Unity and wait for the import to finish, or right-click the workflow in the Project window and choose **Reimport**.
3. **Is the scene using the right asset?** Select the object with **Bonsai Workflow** and click the **Bonsai Workflow Asset** field: the Project window highlights what it uses. It should be the `.bonsai.asset` next to the file you edit. An asset made from a workflow outside **Assets** only updates when you run **Bonsai › Generate workflow asset** ([why](Gotchas.md#workflow-assets-dont-update)).
4. **Did you edit that file?** The highlighted asset is what Unity runs. A copy of the workflow saved somewhere else changes nothing.

## Serial port errors

`Access to the port 'COM5' is denied`: another program has the port open. Close the **Bonsai editor** (a workflow running there holds the port), the **Arduino IDE** and its Serial Monitor, and any other serial program. Stop and restart Play.

`The port 'COM5' does not exist`: the device is on a different port on this computer, or unplugged.

1. Open Windows **Device Manager** › **Ports (COM & LPT)** to see which COM number the device has.
2. Open the workflow in Bonsai and set the node's **PortName** to that number. Save.
3. Click back into Unity and press Play again ([step 12](Getting-Started.md#12-when-you-change-the-workflow)).

Port numbers differ between computers. Every computer needs its own setting.

## Red errors after pasting a script

| Error contains | Cause | Fix |
|---|---|---|
| `The type or namespace name 'Bonsai' could not be found` | Bonsai's DLLs are missing | [Missing DLLs](#missing-bonsai-dlls) |
| `The type or namespace name 'BonsaiWorkflow' could not be found` | The `Assets/Bonsai` folder is missing | Copy it from this project |
| `already contains a definition for` | The same script exists twice (two files with the same class name) | Delete one of them |
| `Unexpected symbol`, `expected` | Part of the code was lost while copying | Select all, delete, and paste the recipe again |
| `The name ... does not exist in the current context` | Code was changed by accident | Paste the recipe again |

Then check the **file name matches the class name**: the line `public class ShowBonsaiValue` must be in a file called `ShowBonsaiValue`. Rename the file in the Project window if not (right-click › **Rename**).

## The associated script can not be loaded

Unity can't use a script on that component. Either scripts have errors (fix the red errors first), or the file name doesn't match the class name (see above). When the Console is clear, the component works again.

## Works in Bonsai, not in Unity

| Node or situation | Why | What to do |
|---|---|---|
| **KeyDown** (keyboard) | Doesn't work inside Unity | Use Unity's keyboard in a script instead; a programmer can replace the node ([Gotchas](Gotchas.md#keydown-never-fires-inside-unity)) |
| Hardware nodes inside a **SelectMany** | They silently do nothing | Move them to the main level ([Gotchas](Gotchas.md#named-resources-inside-selectmany-do-nothing)) |
| Nodes from a package Unity doesn't have | Load as empty nodes | [Missing DLLs](#missing-bonsai-dlls) |
| Visualizer windows (double-click while running) | Bonsai's editor isn't running | Use [Show a value on screen](Recipes.md#show-a-value-on-screen) |
| **BonVision**, VR, shaders, Bonsai audio | Known to have problems in Unity | Do graphics and sound in Unity instead |
| C# scripts inside the workflow (**CSharpSource**, `Extensions` folder) | Not built for Unity (not tested) | Ask a programmer to move the code into a Unity script |

## Workflow stopped after an error

When a node fails, the Console shows the error in red, and that part of the workflow stays stopped until Play is restarted. This is how Bonsai works. Read the error: it usually names the node and the reason (a port, a file path, a value of the wrong type). Fix the cause and press Play again.
