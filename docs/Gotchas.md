# Gotchas

[README](../README.md) › Gotchas

Traps found while running Bonsai 2.9 workflows inside Unity 6, with their fixes. The hooks themselves are in [Hook API](Hook-API.md). Looking up an error message? Start with [Troubleshooting](Troubleshooting.md), which points here where needed.

| Symptom | Section |
|---|---|
| A node does nothing, no error; or `... has no settable property ...` | [A missing DLL makes empty nodes](#a-missing-dll-makes-empty-nodes) |
| Hook fires with the wrong values, or never | [Hooks bind the wrong node](#hooks-bind-the-wrong-node) |
| `Dictionary.TryInsert` exceptions from `Bonsai.Arduino` at start | [The Arduino PinMode race](#the-arduino-pinmode-race) |
| A serial or Arduino node inside a `SelectMany` does nothing | [Named resources inside SelectMany do nothing](#named-resources-inside-selectmany-do-nothing) |
| `KeyDown` works in Bonsai but not in Unity | [KeyDown never fires inside Unity](#keydown-never-fires-inside-unity) |
| Part of the workflow stops after one error | [An error stops that branch](#an-error-stops-that-branch) |
| `No valid build context was provided` | [Build context and double wrapping](#build-context-and-double-wrapping) |
| Callbacks fire twice, or "Workflow already started" | [Play mode without a domain reload](#play-mode-without-a-domain-reload) |
| Edits to the workflow have no effect | [The asset is a snapshot](#the-asset-is-a-snapshot) |
| Which packages Consolidate skips or flags, and why | [Editor-only and incompatible packages](#editor-only-and-incompatible-packages) |

## A missing DLL makes empty nodes

Bonsai loads each node from an assembly it can't find as a *proxy*: same type name, no properties, does nothing. The workflow builds and runs. The only visible symptom is a Unity-side override failing (`SetNodeProperty` logs `Bonsai.IO.CsvWriter has no settable property FileName`).

**Fix:** list every DLL in `Assets/Bonsai/RequiredDlls.txt`, and run **Consolidate packages**. `BonsaiDllCheck` then warns at reload, errors at Play and fails builds when one is missing (see [Checking the DLLs](../README.md#checking-the-dlls)).

## Hooks bind the wrong node

`InjectSubscription` matches the display name **and** the exact type. Names are often shared (a subject and every `SubscribeSubject` that reads it), so passing the wrong type, or a type from an older Bonsai file format, binds a different node or none. The upstream README's `typeof(PublishSubjectBuilder)` never matches a file saved by current Bonsai. See [Which type to pass](Hook-API.md#which-type-to-pass).

Hooks reach the **top level** by default. Pass `groupPath` to reach inside a `GroupWorkflow` (see [Hooks inside groups](Hook-API.md#hooks-inside-groups)). Nothing inside a `SelectMany` can be hooked: route what you need into a subject outside it.

## The Arduino PinMode race

Every `DigitalOutput` on one `CreateArduino` connection calls `PinMode` when it subscribes, without a lock. All nodes subscribe together when the workflow starts, and concurrent `PinMode` calls corrupt the Arduino's pin dictionary: `InvalidOperationException` or `NullReferenceException` from `Dictionary.TryInsert`. Writes are locked and don't race.

**Fix:** put a `DelaySubscription` after each `DigitalOutput`, each at a **different** time (for example 1, 1.25, 1.5, 1.75 s). Two outputs with the same delay bring the errors back. Anything that drives an output before its subscription time is lost, so keep the first event that uses an output later than its delay.

## Named resources inside SelectMany do nothing

A node that refers to a named connection (`DigitalOutput` on an Arduino port name, `SerialReadLine` on a serial port name) inside a `SelectMany`'s inner workflow builds without error and silently does nothing: the inner workflow is built as a separate graph where the name doesn't resolve. `GroupWorkflow` and `Condition` are spliced inline and are fine.

**Fix:** keep such nodes inline; if a `SelectMany` generates the values (a TTL pulse, say), put the hardware node after it, outside.

## KeyDown never fires inside Unity

`Bonsai.Windows.Input.KeyDown` uses a Windows low-level keyboard hook. It works in the Bonsai editor but never emits inside Unity (likely because it needs a Windows message loop on its thread; unconfirmed).

**Fix:** replace the node with a Unity source that watches Unity's own input, using [`ReplaceSource`](Hook-API.md#replacesourcet-swap-a-node-for-a-unity-source). Its element type must be one downstream nodes accept.

## An error stops that branch

`BonsaiWorkflow` logs workflow errors with `Debug.LogError`, but by Rx rules the sequence that threw has already ended and stays ended until Play restarts. Handle expected errors inside the workflow (`Catch`, `Retry`) if a branch must survive them.

## Build context and double wrapping

`StartWorkflow` wraps the graph in inspectable builders **exactly once** (`ToInspectableGraph()`). `ExpressionBuilder.Unwrap` removes only one wrapper, so wrapping twice hides subjects from the build context: `No valid build context was provided`. The upstream package did this; don't reintroduce it.

## Play mode without a domain reload

With *Enter Play Mode Options* set to skip the domain or scene reload, static fields and component fields survive into the next Play session. Upstream, events still queued in `BonsaiDispatcher` were delivered to the new session, and `BonsaiWorkflow` kept its old hook registrations, so each callback ran twice. Both now reset (`BonsaiDispatcher.Initialize`, `BonsaiWorkflow.OnDestroy`). Your own components should reset their runtime state at the top of `Awake` too.

## The asset is a snapshot

Unity runs the `.bonsai.asset`, not the `.bonsai` file. After editing the workflow in Bonsai, run **Bonsai > Create Bonsai workflow asset**, pick the file and choose **Overwrite**. Overwrite updates the asset in place, so scenes keep their reference.

## Editor-only and incompatible packages

The Bonsai editor's own assemblies (`Bonsai.Editor`, `Bonsai.Design` and everything built on them, ScintillaNET, WebView2, DockPanelSuite and similar) need WinForms or WPF, and Unity can't load them. **Consolidate packages** skips them; never copy them in by hand.

Some runtime packages also have known problems in Unity: `Bonsai.BonVision`, `Bonsai.VR`, `Bonsai.Audio`, `Bonsai.Shaders`. Consolidate does not copy them: it logs an error for each and lists them in its dialog. A workflow that uses one of their nodes needs the DLL by hand, and will load those nodes as empty proxies until then. Nothing is deleted automatically.
