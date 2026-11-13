# Possible extensions

[README](../README.md) › Possible extensions

Ideas for integrating Unity more deeply with Bonsai and the packages around it. **Most of this is not built.** Each entry says what it would give users, a sketch of how, the effort (S: a day or two, M: about a week, L: several weeks), and what is unknown. Entries with a **Status** line are built or partly built; everything else is not. Facts about Bonsai and its packages come from the [sources](#sources) at the end. Anything marked *untested* is an expectation, not something we have run.

| Status | Entries |
|---|---|
| Built | [A3](#a3-unity-to-bonsai-subjects), [A6](#a6-reach-nested-nodes) |
| Partly built | [A4](#a4-externalized-properties-in-the-inspector), [A5](#a5-generate-requireddllstxt-from-the-workflow), [A7](#a7-workflow-lifecycle-and-errors-in-unity), [A10](#a10-bonsai-environments-and-pinned-packages), [A12](#a12-built-players) |
| New task | [A14](#a14-unity-created-subjects) (the part of A3 not built) |

## Recommended order

If only a few get built, these give the most for the least:

1. **[Import `.bonsai` files directly](#a1-import-bonsai-files-directly)** (S). Removes the manual "Create asset / Overwrite" step, the most common user mistake.
2. **[No-code Inspector components](#a2-no-code-inspector-components)** (M). Users connect subjects to scene actions without writing scripts; most of the [Recipes](Recipes.md) become components.
3. **[Generate `RequiredDlls.txt` from the workflow](#a5-generate-requireddllstxt-from-the-workflow)** (S). The DLL list stays right without anyone editing it.
4. **[Unity-created subjects](#a14-unity-created-subjects)** (M). Finishes A3: Unity creates the subject itself, so a workflow doesn't have to declare it first.
5. **[Harp clock synchronization](#b2-harp)** (M). Puts Unity's events on the same sub-millisecond clock as Harp, ONIX and other rig hardware.

## A. The bridge itself

### A1. Import `.bonsai` files directly

**Gives:** no more **Bonsai › Create Bonsai workflow asset** and **Overwrite**. Saving the workflow in Bonsai updates Unity automatically.

**How:** a Unity [`ScriptedImporter`](https://docs.unity3d.com/ScriptReference/AssetImporters.ScriptedImporter.html) for the `.bonsai` extension that produces a `BonsaiWorkflowAsset` as the file's main object. The importer reruns whenever the file changes, and the asset's GUID is the `.bonsai` file's own, so references survive. Existing `.bonsai.asset` files need a one-off migration (repoint scene references, then delete them). Ignore `.bonsai.layout` files.

**Effort:** S. **Unknown:** whether Bonsai locks the file while saving (retry on import failure).

### A2. No-code Inspector components

**Gives:** non-programmers wire Bonsai to the scene in the Inspector.

**How:** a small set of components, each with a node picker filled from the assigned workflow (list the top-level subjects and externalized properties by parsing the asset in the editor):

| Component | Replaces recipe |
|---|---|
| `BonsaiSubjectEvent`: subject → `UnityEvent<T>` (int, float, bool, string, or no argument) | Show a value, Do something on an event |
| `BonsaiSubjectToTransform`: number → position/rotation/scale axis with scale and offset | Move an object with a number |
| `BonsaiPropertyInput`: a Unity value (field, `Time.time`, key state) → externalized property | Send a number to Bonsai |
| `BonsaiKeySource`: Unity key → replaces a `KeyDown` node | (new) |
| `BonsaiFileRedirect`: per-session folder for `CsvWriter`, `FileCapture`, `VideoWriter`, Harp `MessageWriter` | Save CSV in a session folder |

Values need converting from boxed `object` to the event's type; a shared `TryConvert<T>` that logs and skips mismatches avoids the dispatcher problem described in [Hook API](Hook-API.md#injectsubscription-bonsai-to-unity).

**Effort:** M. **Unknown:** how to show the node picker before the DLLs are consolidated (fall back to a text field).

### A3. Unity-to-Bonsai subjects

**Gives:** Unity publishes named streams (trial state, player position, frame times) that any part of the workflow, including nested groups, reads with an ordinary `SubscribeSubject`.

**Status: built, for subjects the workflow declares.** `UnitySignal<T>` is a `Source<T>` that Unity calls `Send(value)` on, and `BonsaiWorkflow.SendToSubject<T>(signal, subjectName, groupPath)` feeds it into a subject the workflow already declares (for example a `BehaviorSubject` in the `.bonsai` file). See [Hook API](Hook-API.md#sendtosubjectt-unity-to-bonsai-by-subject). The part not built, where Unity creates the subject itself, is [A14](#a14-unity-created-subjects).

### A4. Externalized properties in the Inspector

**Gives:** experimenters change workflow parameters (delays, thresholds, port names) on the **Bonsai Workflow** component, per scene or per computer, without opening Bonsai.

**Status: partly built.** `GetExternalizedTimeSpan(name)` reads one value, `SetNodeProperty` overrides a property by node type, and `ReplaceSource` swaps a source for a Unity one. Not built: the generic `GetExternalizedValue<T>`, the Inspector fields, per-machine override files, and targeting the mapped node rather than every node of a type.

**How:** list the workflow's top-level `ExternalizedMapping` nodes and the types of the properties they map. Show each as a typed field with the workflow's value as the default; apply changed ones at start with the same mechanism as `SetNodeProperty`, but targeted at the mapped node rather than every node of a type. Generalizes `GetExternalizedTimeSpan` to `GetExternalizedValue<T>(name)`. Per-machine values (COM ports) could live in an untracked override file, which would also fix the "wrong COM port after pulling" problem.

**Effort:** M.

### A5. Generate `RequiredDlls.txt` from the workflow

**Gives:** the DLL check stays correct without hand-editing.

**Status: partly built.** The check itself exists: `RequiredDlls.txt` is read by `BonsaiDllCheck`, which warns in the editor and fails player builds. The list is still edited by hand.

**How:** every `.bonsai` file declares the assemblies it uses in its header (`xmlns:ard="clr-namespace:Bonsai.Arduino;assembly=Bonsai.Arduino"`). Read those from every workflow asset in the project, resolve dependencies from the install's `Bonsai.config` and the DLLs' references (`AssemblyName.GetReferencedAssemblies`), and write the list with versions from the consolidated files. Run it after Consolidate and on import (A1).

**Effort:** S.

### A6. Reach nested nodes

**Gives:** hooks for nodes inside groups, not only the top level. Users wouldn't have to restructure workflows for Unity.

**Status: built.** `InjectSubscription`, `InjectImmediateSubscription`, `SendToSubject`, `ReplaceSource` and `SetNodeProperty` take an optional `groupPath` (`"HARP/Markers"`). The search walks `GroupWorkflowBuilder.Workflow` recursively and skips disabled groups. `SelectMany` and `CreateObservable` inner workflows are rebuilt per value and can't be hooked; this is documented. See [Hooks inside groups](Hook-API.md#hooks-inside-groups). Remaining limit: nodes inside a group are matched by type and display name, so subject names must be unique across the whole workflow. Addressing by full path is not built.

### A7. Workflow lifecycle and errors in Unity

**Gives:** a public `StartWorkflow`/`StopWorkflow`/`Restart`, an `IsRunning` flag, and `UnityEvent`s for **Started**, **Completed** and **Error** (with the failing node's name), so a scene can show "hardware disconnected" or restart cleanly.

**Status: partly built.** `StopWorkflow()` is public and safe to call at any time. `HoldStart`/`ReleaseStart` delay Auto Start. Errors and completion are only logged to the Console. Not built: public `StartWorkflow` and `Restart`, `IsRunning`, the Started, Completed and Error events, and error reporting by node name. `StartWorkflow` is currently private.

**How:** expose what `BonsaiWorkflow` already does; subscribe to each top-level branch's errors separately to report which one ended. A restart needs the workflow re-parsed from the asset, since the built graph has been modified.

**Effort:** S.

### A8. `IncludeWorkflow` support

**Gives:** workflows split into reusable files (`IncludeWorkflow` nodes) work in Unity.

**How:** an included workflow is loaded by its `Path`. In Unity the working directory is the project folder, not the workflow's, so relative paths probably break (*untested*). Resolve paths relative to the source `.bonsai` file at import time (A1) and embed the included XML in the asset, so builds don't need the files.

**Effort:** M. **Unknown:** how Bonsai resolves embedded-resource paths from packages.

### A9. Type converters for common Bonsai data

**Gives:** Bonsai data types usable directly in Unity.

| Bonsai type | Unity side |
|---|---|
| OpenCV `IplImage` (cameras, `Bonsai.Vision`) | `Texture2D`, updated in place without per-frame allocation |
| `Mat` (multichannel signals: ephys, DAQ, audio buffers) | `float[]` / `NativeArray<float>` |
| `Point2f`, pose structures (DeepLabCut, SLEAP) | `Vector2`, a pose component driving transforms |
| Tuples and anonymous types from `ExpressionTransform` | Read by field name |
| `Timestamped<T>`, Harp messages | Value plus timestamp |

**Effort:** M per family. **Unknown:** threading. Image and signal data should be reduced in Bonsai or copied on Bonsai's thread; the dispatcher's once-per-frame delivery is not meant for kHz streams.

### A10. Bonsai environments and pinned packages

**Gives:** one command reproduces a project's Bonsai setup on a new computer.

**Status: partly built.** The installer finds a Bonsai install (a path set in the inspector, then `%LOCALAPPDATA%\Bonsai`, then `%ProgramFiles%\Bonsai`, then a portable copy in the project) and can download and set up a portable copy. Not built: restoring from project-local `Bonsai.config` and `NuGet.config`, or pinning package versions.

**How:** Bonsai can keep a project-local environment: a `.bonsai` folder with `Bonsai.config` (the pinned packages) and `NuGet.config`, created with `bonsai --init` and restored with `bonsai --no-editor` ([environments article](https://bonsai-rx.org/docs/articles/environments.html)). Keep such an environment in the Unity project (both config files in git, `Packages` ignored). Make the installer prefer it, restore it on demand, and consolidate from it, so every machine gets the same versions. Replaces the system-install-first search for projects that opt in.

**Effort:** S to M.

### A11. Shared, strongly typed messages with Sgen

**Gives:** Bonsai and Unity agree on the shape of structured data (trial parameters, events) without hand-written unboxing.

**How:** [`sgen`](https://github.com/bonsai-rx/sgen) generates C# serialization classes from a JSON Schema. Generate one assembly from a schema in the project, use its types in the workflow, and reference the same assembly from Unity scripts. Parameters could be loaded from JSON in Unity and pushed through A3.

**Effort:** M. **Unknown:** the generated code's dependencies and whether they load in Unity.

### A12. Built players

**Gives:** experiments run as a standalone `.exe` without the Unity Editor.

**Status: partly built.** `BonsaiDllBuildCheck` already blocks builds with missing DLLs. No check for the scripting backend exists yet, and no player build has been tested.

**How:** Still to check: whether the native x64 libraries that Consolidate copies reach the build's `Plugins` folder, and the scripting backend. Bonsai builds workflows from expression trees at runtime, which needs the **Mono** backend; IL2CPP has no runtime code generation (*untested*, but likely to fail). Add a build check that refuses IL2CPP, and document a tested build.

**Effort:** S to verify, M to fix.

### A13. Remote mode

**Gives:** a fallback for packages that can't run inside Unity (BonVision, Bonsai's audio and shader packages, Python scripting, anything needing a WinForms message loop): Bonsai runs as its own process, on the same or another PC.

**How:** keep the hook API, but carry subjects over a socket instead of in-process. [`Bonsai.ZeroMQ`](https://github.com/bonsai-rx/zeromq) (asynchronous message streaming) or [`Bonsai.Osc`](https://www.nuget.org/packages/Bonsai.Osc/) on the Bonsai side, and the matching library in Unity. Timestamps must then be converted between clocks (see B2).

**Effort:** L. Adds network latency and jitter, the main thing in-process hosting avoids.

### A14. Unity-created subjects

**Gives:** Unity publishes a named stream without the workflow author declaring a subject for it first. Today the workflow must contain the subject, so adding a Unity value means editing the `.bonsai` file. This is the part of [A3](#a3-unity-to-bonsai-subjects) that is not built.

**How:** at start, before the graph is built, add a top-level node to the graph that is a `UnitySignal<T>` connected to a new `BehaviorSubject` or `PublishSubject` with the requested name. Bonsai's subjects are resolved by name across scopes ([subjects article](https://bonsai-rx.org/docs/articles/subjects.html)), so nested workflows can see it. API sketch: `workflow.PublishToSubject<T>("UnityPosition", out UnitySignal<T> signal)`, registered in `Awake` like the other hooks.

**Effort:** M. **Unknown:** subject declaration order (a subject must exist before `SubscribeSubject` nodes are built), and what to do when the workflow already declares a subject with the same name (reuse it, as `SendToSubject` does, or fail).

## B. Bonsai packages and hardware

Bonsai's package ecosystem is on NuGet and the [bonsai-rx GitHub organization](https://github.com/bonsai-rx). Each package used inside Unity needs its DLLs consolidated and listed in `RequiredDlls.txt`. None of the integrations below has been tested in Unity unless noted.

### B1. Arduino (`Bonsai.Arduino`)

**State:** used in production through this bridge (digital outputs, serial touch input), with two documented traps: the [PinMode race](Gotchas.md#the-arduino-pinmode-race) and [named resources inside `SelectMany`](Gotchas.md#named-resources-inside-selectmany-do-nothing).

**Package:** talks to boards running Firmata; nodes `CreateArduino`, `DigitalInput`, `DigitalOutput`, `InputPullUp`, `AnalogInput`, `AnalogOutput` (PWM), `ServoOutput`, `SendSysex`, `ReceiveSysex` ([API](https://bonsai-rx.org/docs/api/Bonsai.Arduino.html)).

**Extensions:**
- Fix the PinMode race at its root (a lock around `PinMode`, contributed upstream, or a `PinMode`-once wrapper node) instead of staggering subscriptions by hand.
- A Unity "pin" component that drives a `DigitalOutput`/`ServoOutput` from a scene event, and one that exposes a `DigitalInput` as a `UnityEvent<bool>` (built on A2/A3).
- Use `SendSysex`/`ReceiveSysex` for custom firmware messages (rewards with a set duration on the board itself, so timing doesn't depend on the PC).

### B2. Harp

**Package:** [Harp](https://harp-tech.org/) is a family of devices with a binary protocol and **automatic sub-millisecond synchronization across devices** through a shared clock. `Bonsai.Harp` implements the protocol; each device has its own package (`Harp.Behavior`, `Harp.SoundCard`, `Harp.LoadCells`, `Harp.CameraController`, `Harp.TimestampGeneratorGen3`, ...). Needs the FTDI D2XX driver.

**Extensions:**
- **Clock alignment.** Every Harp message carries the device's timestamp. Fit a running map from Unity's `Stopwatch` ticks to Harp seconds, using messages Unity receives (A3/A9) plus a physical check (a Harp digital input wired to a photodiode or an output Unity triggers). Then log every Unity event in Harp time alongside the rig's other data.
- **Harp-native logging.** Write Unity events as Harp messages into the same logs (`MessageWriter`), so analysis tools for Harp data read them too.
- **Devices as stimulus outputs.** For example, play sounds on `Harp.SoundCard` from Unity events for hardware-timed audio, instead of the PC sound card.

**Unknown:** FTDI driver loading inside Unity; message rates at the dispatcher.

### B3. Open Ephys ONIX (`OpenEphys.Onix1`)

**Package:** acquisition from the ONIX PCIe system: Neuropixels, Intan-based headstages, Miniscopes and more, with **hardware synchronization across all data streams**, closed-loop feedback in about 100 µs, torque-free commutation, and Bonsai 2.9 or later ([docs](https://open-ephys.github.io/bonsai-onix1-docs/)). Supersedes the deprecated `Bonsai.ONIX`.

**Extensions:**
- **Closed-loop VR.** Detect events in Bonsai (spikes over threshold, a band-power crossing, a decoded position) and send only the result to Unity, which changes the scene. Raw neural data stays in Bonsai and on disk; Unity's frame rate (60 to 144 Hz) is the limit on visual feedback, not ONIX.
- **Shared timebase.** Record a sync signal from Unity (frame flips via a photodiode, or an output pulse through an Arduino or Harp device) on an ONIX input, so each Unity event can be placed on the ONIX clock in analysis.
- **Session control.** Start and stop acquisition and set file names per session from Unity, like the CSV recipe (A2's file redirect).

**Unknown:** whether the ONIX driver and its native libraries load inside Unity. Data rates are high: test with the full headstage configuration.

### B4. Open Ephys GUI

Users who record with the Open Ephys GUI rather than Bonsai can still exchange events with it from Bonsai, through a network plugin (the ONIX docs mention combining Bonsai with the GUI via the **Ephys Socket** plugin). A Unity integration would go through Bonsai or A13's remote mode. **Unknown:** the exact plugin to use for events from Unity; investigate before committing.

### B5. Cameras and video

**Packages:** `Bonsai.Vision` (OpenCV image processing), `Bonsai.Video` (DirectShow), `Bonsai.Spinnaker` (FLIR), `Bonsai.Pylon` (Basler), plus PCO and Emergent ([bonsai-rx organization](https://github.com/bonsai-rx)).

**Extensions:**
- Show a live camera image in a Unity scene or debug overlay (A9's `IplImage` → `Texture2D`).
- Track the animal in Bonsai (thresholding, centroid) and drive the Unity scene from the position.
- Hardware-trigger cameras from Unity frames for exact frame-to-video alignment.

**Unknown:** SDK native libraries inside Unity; copying frames at full resolution and rate.

### B6. Pose estimation: DeepLabCut and SLEAP

**Packages:** [`Bonsai.DeepLabCut`](https://github.com/bonsai-rx/deeplabcut) (real-time pose estimation with pretrained models; needs TensorFlow 2.8) and [`Bonsai.Sleap`](https://github.com/bonsai-rx/sleap) (real-time multi-animal pose tracking).

**Extensions:** drive a Unity avatar or the virtual camera from body-part positions; trigger events on posture. A pose component (A9) maps named body parts to transforms.

**Unknown:** TensorFlow's native DLLs and GPU runtime inside Unity's process (likely the hardest of all packages to host; A13 is the fallback).

### B7. Other acquisition hardware

| Package | What it gives Unity |
|---|---|
| `Bonsai.DAQmx` (National Instruments) | Analog and digital I/O, hardware-timed outputs triggered by scene events |
| `Bonsai.LabJack` | Low-cost analog and digital I/O |
| `Bonsai.BrainFlow` | Consumer biosensors (EEG, EMG) for closed-loop games |
| `Bonsai.Ephys` | Electrophysiology signal processing |
| `Bonsai.Mixer` | Multichannel audio capture and playback |

For each: consolidate, list the DLLs, test the driver loads inside Unity, and add a recipe.

### B8. Machine learning and scripting

| Package | Use with Unity | Note |
|---|---|---|
| [`Bonsai.ML`](https://github.com/bonsai-rx/machinelearning) | Online decoders and state-space models running beside the scene, results sent to Unity | Check native dependencies |
| [`Bonsai.Scripting.Python`](https://github.com/bonsai-rx/python-scripting) | Python code inside the workflow | Hosts a Python runtime in the process; inside Unity it is *untested* and risky. Prefer A13. |
| `Bonsai.Scripting.Expressions` | Inline expressions (`ExpressionTransform`) | Already works in Unity (used in production) |
| C# extensions (`CSharpSource`, `Extensions` folder) | Custom nodes | Compiled by the Bonsai editor when a workflow loads, so they won't exist in Unity (*untested*). A Unity-side build step could compile them into a DLL in `Assets/Packages`. |

### B9. Packages that overlap with Unity

`BonVision` (2D/3D visual stimuli and VR built on Bonsai's shader packages), `Bonsai.Shaders`, `Bonsai.Audio` and `Bonsai.VR` already do in Bonsai what Unity does, and are flagged by Consolidate as having problems inside Unity. Don't integrate them; render and play sound in Unity. If a lab must keep a BonVision stimulus, run it out of process (A13).

## C. Tooling

| Idea | Gives | Effort |
|---|---|---|
| **Workflow viewer in Unity**: a read-only graph of the asset (nodes, names, types) in an editor window | See which names and types to hook without opening Bonsai | M |
| **Live monitor window**: every top-level subject's latest value and rate while playing | Debug without visualizers | S |
| **Test mode**: replace hardware nodes with simulated sources from a CSV | Run a rig scene on a laptop with no hardware | M |
| **Unity Package Manager package** (`package.json`, Git URL install) instead of copying `Assets/Bonsai` | One-click install and updates for users | S |
| **Release `.unitypackage` built by CI** | The README's download link points to something current | S |
| **Upstream fixes**: report the PinMode race and the `KeyDown` behavior to the Bonsai and Arduino package maintainers | Fewer workarounds | S |

## Sources

- Bonsai documentation: [editor](https://bonsai-rx.org/docs/articles/editor.html), [installation](https://bonsai-rx.org/docs/articles/installation.html), [packages](https://bonsai-rx.org/docs/articles/packages.html), [subjects](https://bonsai-rx.org/docs/articles/subjects.html), [property mapping](https://bonsai-rx.org/docs/articles/property-mapping.html), [environments](https://bonsai-rx.org/docs/articles/environments.html), [scripting extensions](https://bonsai-rx.org/docs/articles/scripting-extensions.html)
- Bonsai API reference: [Bonsai.Reactive](https://bonsai-rx.org/docs/api/Bonsai.Reactive.html), [Bonsai.Arduino](https://bonsai-rx.org/docs/api/Bonsai.Arduino.html)
- [bonsai-rx GitHub organization](https://github.com/bonsai-rx) (package repositories)
- Harp: [harp-tech.org](https://harp-tech.org/), [Harp packages on NuGet](https://www.nuget.org/profiles/harp)
- Open Ephys ONIX: [OpenEphys.Onix1 docs](https://open-ephys.github.io/bonsai-onix1-docs/)
- [BonVision](https://bonvision.github.io/)
- Unity: [ScriptedImporter](https://docs.unity3d.com/ScriptReference/AssetImporters.ScriptedImporter.html)
