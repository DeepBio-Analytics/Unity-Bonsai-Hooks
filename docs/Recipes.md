# Recipes

[README](../README.md) › Recipes

Ready-made scripts for common jobs. Each one is complete: copy it, don't edit the code, and change its settings in the Inspector. Do [Getting started](Getting-Started.md) first; this page uses the same steps and window names.

| Recipe | Direction | Use it to... |
|---|---|---|
| [Show a value on screen](#show-a-value-on-screen) | Bonsai → Unity | Check what a subject is sending |
| [Move an object with a number](#move-an-object-with-a-number) | Bonsai → Unity | Make a sensor (e.g. an Arduino analog pin) move something in the scene |
| [Do something when Bonsai sends an event](#do-something-when-bonsai-sends-an-event) | Bonsai → Unity | Play a sound, show or hide an object, when a trial ends, a lick is detected, ... |
| [Send a number from Unity to Bonsai](#send-a-number-from-unity-to-bonsai) | Unity → Bonsai | Let the scene change a setting in the workflow |
| [Save Bonsai's CSV file in a session folder](#save-bonsais-csv-file-in-a-session-folder) | Unity → Bonsai | Keep each run's data in its own dated folder |
| [Stop the workflow with a key](#stop-the-workflow-with-a-key) | Unity → Bonsai | Close the hardware connections before quitting |

## How to add any recipe

Every recipe goes on the **same object as the Bonsai Workflow component**.

1. **Make the script file.** Project window › right-click › **Create › Scripting › MonoBehaviour Script**. Name it **exactly** as the recipe's heading says (for example `MoveWithBonsaiNumber`), then press Enter.
2. **Paste the code.** Double-click the file, select all (Ctrl+A), delete, paste the recipe, save (Ctrl+S).
3. **Let Unity compile.** Switch back to Unity and wait for the spinner (bottom right) to stop. The Console must show **no red errors**. If it does, see [Troubleshooting › Red errors after pasting a script](Troubleshooting.md#red-errors-after-pasting-a-script).
4. **Attach it.** Select the object with **Bonsai Workflow** in the Hierarchy, and drag the script from the Project window onto the bottom of the Inspector.
5. **Fill in its settings** in the Inspector, as the recipe says.
6. **Stop Play before changing settings.** Changes made while playing are thrown away when you stop.

Several recipes can sit on the same object at once.

**The name rule.** A recipe that listens to Bonsai finds the subject by its **Name** in Bonsai. The Name must be on a **PublishSubject** node on the **main level** of the workflow (not inside a group), and must match exactly, capitals included. Using a BehaviorSubject or another kind of subject? See [Hook API › Which type to pass](Hook-API.md#which-type-to-pass); the recipe's `typeof(Bonsai.Reactive.PublishSubject)` must change to match.

---

## Show a value on screen

**What it does:** writes the latest value of one subject in the top-left corner of the Game view.

**In Bonsai:** any stream connected to a **PublishSubject** with a Name.

**Script name:** `ShowBonsaiValue`

```csharp
using UnityEngine;

[RequireComponent(typeof(BonsaiWorkflow))]
public class ShowBonsaiValue : MonoBehaviour
{
    // The Name you gave the PublishSubject in Bonsai. Change it in the Inspector.
    public string subjectName = "TimerOutput";

    private string latestValue = "(nothing yet)";

    void Awake()
    {
        var workflow = GetComponent<BonsaiWorkflow>();
        workflow.InjectSubscription(subjectName, typeof(Bonsai.Reactive.PublishSubject), value =>
        {
            latestValue = value == null ? "null" : value.ToString();
        });
    }

    void OnGUI()
    {
        GUI.Label(new Rect(20, 20, 600, 40), $"{subjectName}: {latestValue}");
    }
}
```

| Setting | Set it to |
|---|---|
| Subject Name | The subject's Name in Bonsai |

To show two subjects, add the script twice; the labels overlap, so this is only for checking.

---

## Move an object with a number

**What it does:** moves an object up and down according to a number from Bonsai, for example an analog sensor value.

**In Bonsai (Arduino example):**

1. The Arduino must run **StandardFirmata**: in the Arduino IDE, **File › Examples › Firmata › StandardFirmata**, then **Upload**. Close the Arduino IDE afterwards (it holds the port).
2. In Bonsai, install the **Bonsai - Arduino Library** package (**Manage packages**, search `Arduino`).
3. Build: **AnalogInput** → **PublishSubject** (Name `Sensor`).
4. Select **AnalogInput**. Set **PortName** to your Arduino's port (`COM3`, for example; Windows Device Manager › Ports shows it) and **Pin** to the analog pin (0 for A0).
5. Save into **Assets**, then in Unity consolidate packages ([how](Getting-Started.md#if-your-workflow-uses-more-bonsai-packages)).

`AnalogInput` sends whole numbers from 0 to 1023.

**Script name:** `MoveWithBonsaiNumber`

```csharp
using UnityEngine;

[RequireComponent(typeof(BonsaiWorkflow))]
public class MoveWithBonsaiNumber : MonoBehaviour
{
    private BonsaiWorkflow workflow;

    // The Name you gave the PublishSubject in Bonsai.
    public string subjectName = "Sensor";

    // The object to move (drag it here).
    public Transform objectToMove;

    // How far the object moves for each unit of the Bonsai number.
    public float scale = 0.01f;

    private float latestNumber;

    void Awake()
    {
        workflow = GetComponent<BonsaiWorkflow>();
        workflow.InjectSubscription(subjectName, typeof(Bonsai.Reactive.PublishSubject), value =>
        {
            // Bonsai can send whole numbers or decimals; anything else is ignored.
            if (value is int whole) latestNumber = whole;
            else if (value is long big) latestNumber = big;
            else if (value is float single) latestNumber = single;
            else if (value is double precise) latestNumber = (float)precise;
            else Debug.LogWarning($"{subjectName} sent {value}, which is not a number.");
        });
    }

    void Update()
    {
        Vector3 position = objectToMove.localPosition;
        position.y = latestNumber * scale;
        objectToMove.localPosition = position;
    }
}
```

| Setting | Set it to |
|---|---|
| Subject Name | `Sensor` (or your Name) |
| Object To Move | Drag the object to move from the Hierarchy (for example a Cube: Hierarchy **+ › 3D Object › Cube**) |
| Scale | How far, in Unity units (metres), one step of the number moves it. With 0 to 1023 and `0.01`, the object moves up to about 10 m. |

**Try it without an Arduino:** use the workflow from Getting started (Timer → `MyNumbers`) and set Subject Name to `MyNumbers`; the object rises one step per second.

---

## Do something when Bonsai sends an event

**What it does:** every time the subject sends anything, it runs the actions you pick in the Inspector, with no more code. The value itself is ignored.

**In Bonsai:** connect whatever marks the event (a trial ending, a lick, a button) to a **PublishSubject**, for example Name `Reward`. To react to only some values, filter in Bonsai first (for example with a **Condition**).

**Script name:** `ReactToBonsaiEvent`

```csharp
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(BonsaiWorkflow))]
public class ReactToBonsaiEvent : MonoBehaviour
{
    private BonsaiWorkflow workflow;

    // The Name you gave the PublishSubject in Bonsai.
    public string subjectName = "Reward";

    // What should happen each time Bonsai sends something. Click + in the Inspector to add actions.
    public UnityEvent whenBonsaiSends;

    void Awake()
    {
        workflow = GetComponent<BonsaiWorkflow>();
        workflow.InjectSubscription(subjectName, typeof(Bonsai.Reactive.PublishSubject), value =>
        {
            whenBonsaiSends.Invoke();
        });
    }
}
```

**Choosing the actions:**

1. In the **When Bonsai Sends ()** box in the Inspector, click **+**.
2. Drag the object to act on from the Hierarchy into the empty slot (it says **None (Object)**).
3. Click the drop-down (it says **No Function**) and choose what to do. Common choices:

| To... | Object to drag | Function to choose |
|---|---|---|
| Play a sound | An object with an **Audio Source** (add one with **Add Component › Audio Source** and set its **Audio Resource** to a sound file; untick **Play On Awake**) | **AudioSource › Play ()** |
| Show an object | The object | **GameObject › SetActive (bool)**, then tick the box |
| Hide an object | The object | **GameObject › SetActive (bool)**, box unticked |

Add several entries to do several things at once.

---

## Send a number from Unity to Bonsai

**What it does:** sends a number from Unity into the workflow every frame. The example sends an object's height; change what it sends by editing the one line marked below.

**In Bonsai:** the number arrives by setting a node's property. You choose which property by *externalizing* it:

1. Add the node whose property Unity should set. To simply receive the number, use **FloatProperty**.
2. Right-click the node › **Externalize Property** › choose the property (**Value** for FloatProperty). A new small node appears, connected into it.
3. Select the new node. In **Properties**, set its display name to something clear, for example `FloatInput`. Unity finds it by this name.
4. Connect **FloatProperty** onwards to whatever uses the number, or to a **PublishSubject** to see it in Unity.

The sample workflow **input-test.bonsai** (in **Assets › Samples › Workflows**) is already built this way: `FloatInput` → **FloatProperty** → **PublishSubject** `FloatValue`. Open it in Bonsai to compare.

**Script name:** `SendNumberToBonsai`

```csharp
using System;
using System.Reactive.Subjects;
using UnityEngine;

[RequireComponent(typeof(BonsaiWorkflow))]
public class SendNumberToBonsai : MonoBehaviour
{
    private BonsaiWorkflow workflow;

    // The name of the externalized property in Bonsai (its DisplayName).
    public string externalizedPropertyName = "FloatInput";

    // The object whose height is sent to Bonsai (drag it here).
    public Transform objectToWatch;

    private UnityValueSource source;

    void Awake()
    {
        workflow = GetComponent<BonsaiWorkflow>();
        source = new UnityValueSource();
        workflow.InjectInput(source, externalizedPropertyName);
    }

    void Update()
    {
        // Sends one value per frame
        source.Send(objectToWatch.position.y);
    }

    // Turns values sent from Unity into a Bonsai source. Leave this part as it is.
    class UnityValueSource : Bonsai.Source<float>
    {
        private readonly Subject<float> values = new Subject<float>();

        public void Send(float value)
        {
            values.OnNext(value);
        }

        public override IObservable<float> Generate()
        {
            return values;
        }
    }
}
```

| Setting | Set it to |
|---|---|
| Externalized Property Name | The display name from step 3 (`FloatInput`) |
| Object To Watch | Drag the object whose height to send |

**To send something else,** change only `objectToWatch.position.y` in `Update`, for example `objectToWatch.position.x` for left-right, or `Time.time` for seconds since Play.

**Check it:** add [Show a value on screen](#show-a-value-on-screen) with Subject Name `FloatValue` and move the object in the Scene view while playing.

Some Bonsai nodes, such as **Timer**, read a property only when the workflow starts, so changing it later has no effect. Use this recipe with properties that are read on every value.

---

## Save Bonsai's CSV file in a session folder

**What it does:** each time you press Play, creates a folder named with the date and time (for example `C:\Data\2026-11-02_14-30-05`) and makes every **CsvWriter** in the workflow save into it as `bonsai.csv`. Without this, every run writes wherever the workflow says, and may overwrite the last one.

**In Bonsai:** your workflow has a **CsvWriter** (package **Bonsai - System Library**) on the main level. Its own FileName doesn't matter; Unity replaces it. Consolidate packages after installing the package ([how](Getting-Started.md#if-your-workflow-uses-more-bonsai-packages)).

**Script name:** `SaveBonsaiCsvToFolder`

```csharp
using System;
using System.IO;
using UnityEngine;

[RequireComponent(typeof(BonsaiWorkflow))]
public class SaveBonsaiCsvToFolder : MonoBehaviour
{
    private BonsaiWorkflow workflow;

    // The folder sessions are saved in. Each Play creates a new dated folder inside it.
    public string dataFolder = @"C:\Data";

    void Awake()
    {
        workflow = GetComponent<BonsaiWorkflow>();
        string sessionFolder = Path.Combine(dataFolder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        Directory.CreateDirectory(sessionFolder);

        // Every CsvWriter in the workflow writes here, without a date added to the name
        workflow.SetNodeProperty("Bonsai.IO.CsvWriter", "FileName", Path.Combine(sessionFolder, "bonsai.csv"));
        workflow.SetNodeProperty("Bonsai.IO.CsvWriter", "Suffix", "None");

        Debug.Log($"Bonsai data for this session goes to {sessionFolder}");
    }
}
```

| Setting | Set it to |
|---|---|
| Data Folder | The folder to keep sessions in. It is created if it doesn't exist. |

**Check it:** the Console prints `Bonsai data for this session goes to ...` when you press Play. Open that folder after stopping.

If the Console says `Bonsai.IO.CsvWriter has no settable property FileName`, `Bonsai.System.dll` is missing: consolidate packages. If it says `No Bonsai.IO.CsvWriter node found`, the CsvWriter is inside a group or missing.

With two CsvWriters, both would write the same file. Give each its own folder by hand in Bonsai instead, or ask a programmer to extend the recipe.

---

## Stop the workflow with a key

**What it does:** pressing a key (Escape by default) stops the workflow, which closes its serial ports and Arduino connections. Stopping Play does this too; this is for stopping Bonsai while Unity keeps running.

**Script name:** `StopBonsaiWithKey`

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(BonsaiWorkflow))]
public class StopBonsaiWithKey : MonoBehaviour
{
    private BonsaiWorkflow workflow;

    // The key that stops the workflow.
    public Key stopKey = Key.Escape;

    void Awake()
    {
        workflow = GetComponent<BonsaiWorkflow>();
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard[stopKey].wasPressedThisFrame)
        {
            workflow.StopWorkflow();
            Debug.Log("Bonsai workflow stopped. Press Play twice to start it again.");
        }
    }
}
```

| Setting | Set it to |
|---|---|
| Stop Key | The key to use |

Click inside the **Game** view first, or Unity won't see the key. The workflow can't be restarted until you stop and start Play.

This uses Unity's Input System package (`com.unity.inputsystem`), which the sample project includes and has set as its **Active Input Handling**. In your own project, install it in **Window › Package Manager** (search `Input System`) and check **Edit › Project Settings › Player › Other Settings › Active Input Handling** is **Input System Package (New)** or **Both**; restart Unity when asked. If it is set to the old handler only, the script doesn't compile. Don't mix this with scripts that call `Input.GetKey...` unless the setting is **Both**: with the new handler only, those calls throw an `InvalidOperationException` about `UnityEngine.Input`.
