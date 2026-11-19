/*
    Sample: Unity and an Arduino, both directions. Runs arduino-blink.bonsai. Used by ArduinoBlink.

    A Firmata Arduino blinks its built-in LED (pin 13). Space starts and stops the blinking, and Bonsai reports
    the blink rate it measures back to the Console.

      Direction        Hook                  Workflow node                    What happens
      ---------------  --------------------  -------------------------------  ----------------------------------------
      Unity to Bonsai  SendToSubject         BlinkEnabled (PublishSubject)    Space sends true / false: start / stop
      Bonsai to Unity  InjectSubscription    BlinkRate (PublishSubject)       Measured blink rate in Hz, logged
      Unity to Bonsai  SetNodeProperty       Timer.DueTime / Period           Inspector "Blink Period Seconds"

    Needs the Bonsai Arduino and Bonsai.Scripting.Expressions packages consolidated into Assets/Packages, and
    StandardFirmata on the board (see docs/Recipes.md, Arduino example).

    To let Unity change the rate while running later: put an ExternalizedMapping on the Timer's Period in
    arduino-blink.bonsai (it feeds the Timer, as LedOnTime feeds Delay in hooks-demo.bonsai) and use
    BonsaiWorkflow.InjectInput with a Source<TimeSpan>, as HooksDemo does for its slider.
*/

using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drives <c>arduino-blink.bonsai</c>: Space toggles the blinking through a <see cref="UnitySignal{T}"/> sent to the
/// <c>BlinkEnabled</c> subject, and the <c>BlinkRate</c> subject's values are logged. The scene needs only this
/// component and the <see cref="BonsaiWorkflow"/> it points at, with Auto Start on.
/// </summary>
public class ArduinoBlink : MonoBehaviour
{
    public BonsaiWorkflow Workflow;

    [Tooltip("Seconds for one full on-and-off cycle. 1 gives a 1 Hz blink. Written into the workflow's Timer before it starts.")]
    [Min(0.05f)]
    public float BlinkPeriodSeconds = 1f;

    [Tooltip("Log every value Bonsai writes to the LED pin (twice per cycle while blinking). Turn on to see whether stopping really sends false.")]
    public bool LogLedWrites;

    // Unity -> Bonsai
    private readonly UnitySignal<bool> enabledSignal = new UnitySignal<bool>();

    private bool blinking;
    private double lastLoggedRate = double.NaN;   // rounded, so timer jitter doesn't flood the Console

    void Awake()
    {
        // Play mode may skip the domain/scene reload, so reset everything here
        blinking = false;
        lastLoggedRate = double.NaN;

        if (!RequiredAssembliesLoaded())
        {
            return;
        }

        // The Timer ticks once per half cycle. DueTime equals Period so the first measured interval is a full one.
        var half = TimeSpan.FromSeconds(BlinkPeriodSeconds / 2.0);
        Workflow.SetNodeProperty("Bonsai.Reactive.Timer", "DueTime", half);
        Workflow.SetNodeProperty("Bonsai.Reactive.Timer", "Period", half);

        // Unity -> Bonsai: true / false into BlinkEnabled. The workflow keeps the LED low until it is true.
        Workflow.SendToSubject(enabledSignal, "BlinkEnabled");

        // Bonsai -> Unity, on the main thread: the blink rate Bonsai measured, in Hz. It jitters by a few percent,
        // so log it to one decimal and only when it changes or blinking restarts.
        Workflow.InjectSubscription("BlinkRate", typeof(Bonsai.Reactive.PublishSubject), value =>
        {
            if (!blinking || !(value is double hz))
            {
                return;
            }
            double rounded = Math.Round(hz, 1);
            if (rounded != lastLoggedRate)
            {
                lastLoggedRate = rounded;
                Debug.Log($"Blink rate: {rounded:0.0} Hz (measured by Bonsai)");
            }
        });

        // Bonsai -> Unity: what the workflow hands to DigitalOutput. Compare with the physical LED to tell a
        // workflow problem (a wrong value here) from a hardware problem (right value, LED doesn't follow).
        Workflow.InjectSubscription("LedState", typeof(Bonsai.Reactive.PublishSubject), value =>
        {
            if (LogLedWrites)
            {
                Debug.Log($"LED pin <- Bonsai: {value}");
            }
        });

        Debug.Log($"ArduinoBlink: press Space to start and stop the LED. The port and baud rate are set on the CreateArduino node in arduino-blink.bonsai. Opening the port resets the Arduino, so allow about 2 s after Play.");
    }

    void Update()
    {
        var keyboard = Keyboard.current;

        if (keyboard == null || !keyboard.spaceKey.wasPressedThisFrame)
        {
            return;
        }

        blinking = !blinking;
        lastLoggedRate = double.NaN;   // report the rate again as soon as blinking restarts
        enabledSignal.Send(blinking);
        Debug.Log(blinking ? "Blinking started (BlinkEnabled = true)" : "Blinking stopped (BlinkEnabled = false)");
    }

    void OnGUI()
    {
        GUI.Label(new Rect(20, 20, 700, 60),
            $"Arduino blink   period {BlinkPeriodSeconds:0.##} s   {(blinking ? "BLINKING" : "stopped")}\n" +
            "Space: start / stop");
    }

    // A missing package doesn't stop the workflow loading; Bonsai swaps its nodes for proxies that do nothing
    // (see BonsaiDllCheck). Say so here instead of leaving a silent, dark LED.
    private static bool RequiredAssembliesLoaded()
    {
        string[] required = { "Bonsai.Arduino", "Bonsai.Scripting.Expressions" };
        var missing = required.Where(name => !CanLoad(name)).ToArray();
        if (missing.Length == 0)
        {
            return true;
        }
        Debug.LogError($"ArduinoBlink needs {string.Join(" and ", missing.Select(m => m + ".dll"))} in Assets/Packages. " +
                       "Install the packages in Bonsai (Manage packages), then select Assets/Bonsai/BonsaiInstaller and click Consolidate packages.");
        return false;
    }

    // Assembly.Load finds a DLL in Assets/Packages even when nothing in the project references it yet.
    private static bool CanLoad(string assemblyName)
    {
        try
        {
            return System.Reflection.Assembly.Load(assemblyName) != null;
        }
        catch (System.IO.IOException)
        {
            return false;
        }
    }
}
