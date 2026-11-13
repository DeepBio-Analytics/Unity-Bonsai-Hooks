# Glossary

[README](../README.md) › Glossary

Words used in these docs, in plain language.

## Bonsai words

| Word | Meaning |
|---|---|
| **Bonsai** | A free Windows program for building experiments visually, by connecting boxes. [bonsai-rx.org](https://bonsai-rx.org) |
| **Workflow** | A Bonsai diagram: boxes connected by arrows. Saved as a `.bonsai` file. |
| **Node** (or operator) | One box in a workflow. Each does one job: wait, count, read a sensor, write a file. |
| **Toolbox** | The list of nodes on the left of the Bonsai editor, with a search box. |
| **Properties** | The settings of the selected node, shown on the right of the Bonsai editor. |
| **Stream** (or sequence) | The values that flow along an arrow, one after another, over time. |
| **Subject** | A named stream that other parts of the workflow, and Unity, can listen to by name. |
| **PublishSubject** | The most common subject. Listeners get the values sent after they start listening. |
| **BehaviorSubject** | A subject that also remembers its latest value and gives it to new listeners straight away. |
| **SubscribeSubject** | A node that listens to a subject by name inside the workflow. It has the same name as its subject, which is why Unity scripts also say *which kind* of node to find. |
| **Main level** (top level) | The workflow you see when you open the file, not the inside of a group. Hooks reach nodes here by default, and inside a group when given its `groupPath`. |
| **Group** | A box that contains its own small workflow; double-click to open it. |
| **Externalized property** | A node setting pulled out into its own small node, so something else (another node, or Unity) can change it while running. Right-click a node › **Externalize Property**. |
| **Package** | An add-on for Bonsai containing more nodes (Arduino, cameras, file writing...). Installed with **Manage packages**. |
| **Firmata / StandardFirmata** | The program an Arduino must run for Bonsai's Arduino nodes to control it. Uploaded from the Arduino IDE. |

## Unity words

| Word | Meaning |
|---|---|
| **Unity Editor** | The program you build and run scenes in. **Unity Hub** installs and opens it. |
| **Project** | A Unity folder with an `Assets` folder, a `ProjectSettings` folder and more. Here, `BonsaiInterface`. |
| **Assets** | Your project's files: scenes, scripts, sounds, workflow assets. Shown in the **Project** window. |
| **Scene** | One level or screen, containing objects. Saved as a `.unity` file. |
| **GameObject** (object) | Anything in a scene, listed in the **Hierarchy**. |
| **Component** | A piece of behaviour attached to an object, shown in the **Inspector**. **Bonsai Workflow** is one. |
| **Inspector** | The window showing the selected object's components and settings. |
| **Hierarchy** | The window listing the objects in the open scene. |
| **Console** | The window where messages, warnings (yellow) and errors (red) appear. |
| **Play mode** | What happens after pressing ▶: the scene runs. Changes made during Play mode are undone when you stop. |
| **Script** | A text file of C# code (`.cs`) that adds behaviour. Attached to an object like a component. |
| **Compile** | Unity turning scripts into something it can run. Happens automatically after you save a script; red errors stop it. |

## Words from this package

| Word | Meaning |
|---|---|
| **Workflow asset** | Unity's copy of a `.bonsai` file (`name.bonsai.asset`), made with **Bonsai › Create Bonsai workflow asset**. Unity runs this copy, so it must be refreshed after every change to the `.bonsai` file. |
| **Bonsai Workflow** (component) | Runs one workflow asset in the scene. **Auto Start** must be ticked for it to run. |
| **BonsaiInstaller** | The file in `Assets/Bonsai` whose Inspector finds Bonsai and copies its files into Unity. |
| **DLL** | A file of compiled program code (`.dll`). Bonsai's nodes live in DLLs, and Unity needs copies of them. |
| **Consolidate packages** | The BonsaiInstaller button that copies Bonsai's DLLs into `Assets/Packages`. Do it after installing Bonsai packages and on every new computer. |
| **RequiredDlls.txt** | The list of DLLs Unity checks for. Missing ones are reported, because Bonsai would otherwise silently skip their nodes. |
| **Hook** | A way for a Unity script to connect to the workflow: listen to a subject, send in a value, change a setting. See [Hook API](Hook-API.md). |
