/*
    Sample: every hook BonsaiWorkflow offers, in one scene. Runs hooks-demo.bonsai. Used by HooksDemo.

      Hook                          Workflow node                     In the scene
      ----------------------------  --------------------------------  ------------------------------------------
      HoldStart / ReleaseStart      (the whole workflow)              nothing runs until you click Start
      SetNodeProperty               top-level Timer.Period            "Blink period" in the inspector
      GetExternalizedTimeSpan       LedOnTime (Delay.DueTime)         panel shows how long the LED stays on
      InjectSubscription            LedOn, LedOff                     the LED sphere lights and goes dark
      InjectImmediateSubscription   LedOn                             panel compares Bonsai-thread vs frame delay
      SendToSubject                 UnityKey -> KeyEcho               letter/digit keys go in, come back in the log
      InjectInput                   SliderInput -> SliderEcho         the slider drives a FloatProperty
      ReplaceSource + groupPath     TrialTrigger/Timer -> Trigger     Space replaces Bonsai's 3 s timer
      StopWorkflow                  (the whole workflow)              Stop button
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using Bonsai;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Demonstrates every Unity &lt;-&gt; Bonsai hook against <c>hooks-demo.bonsai</c>. Builds its own two spheres
/// (LED and trigger flash) and draws its controls with IMGUI, so the scene needs only this component and the
/// <see cref="BonsaiWorkflow"/> it points at. Leave that workflow's Auto Start on: this script holds the
/// start (<see cref="BonsaiWorkflow.HoldStart"/>) until the Start button is clicked.
/// </summary>
public class HooksDemo : MonoBehaviour
{
    public BonsaiWorkflow Workflow;

    [Tooltip("Pushed into the top-level Timer's Period with SetNodeProperty when the workflow starts.")]
    public float BlinkPeriodSeconds = 0.5f;

    // Unity -> Bonsai
    private readonly UnitySignal<string> keySignal = new UnitySignal<string>();   // SendToSubject
    private readonly UnitySignal<long> triggerSignal = new UnitySignal<long>();   // ReplaceSource
    private FloatSource sliderSource;                                             // InjectInput

    // Scene objects, built in Awake
    private Renderer led, trigger;
    private static readonly Color LedOff = new Color(0.15f, 0.05f, 0.05f), LedOn = new Color(1f, 0.15f, 0.1f);
    private static readonly Color TriggerIdle = new Color(0.1f, 0.1f, 0.2f), TriggerHit = new Color(0.3f, 0.6f, 1f);
    private float triggerFlashUntil;

    // Panel state
    private enum State { Held, Running, Stopped }
    private State state;
    private float slider, sentSlider = -1;
    private TimeSpan ledOnTime;
    private long triggerCount;
    private double queuedDelayMs, immediateDelayMs;
    private int bonsaiThreadId;
    private long immediateEmitTicks;   // written on Bonsai's thread, read on the main thread
    private readonly List<string> log = new List<string>();
    private Vector2 logScroll;

    void Awake()
    {
        // Play mode may skip the domain/scene reload, so reset everything here
        state = State.Held;
        log.Clear();
        triggerCount = 0;
        sliderSource = new FloatSource();
        BuildScene();

        // Keep Auto Start from starting the workflow until the Start button releases it
        Workflow.HoldStart(this);

        // Override a node property before the build. Only the top-level Timer is hit: SetNodeProperty
        // without a groupPath never looks inside groups, so TrialTrigger's Timer keeps its 3 s.
        Workflow.SetNodeProperty("Bonsai.Reactive.Timer", "Period", TimeSpan.FromSeconds(BlinkPeriodSeconds));

        // Bonsai -> Unity, on the main thread: drive the LED. The timestamp is when Bonsai emitted.
        Workflow.InjectSubscription("LedOn", typeof(Bonsai.Reactive.PublishSubject), (value, ticks) =>
        {
            SetColor(led, LedOn);
            queuedDelayMs = MsSince(ticks);
        });
        Workflow.InjectSubscription("LedOff", typeof(Bonsai.Reactive.PublishSubject), value => SetColor(led, LedOff));

        // Bonsai -> Unity, at once on Bonsai's thread. No Unity API in here: just record and leave.
        Workflow.InjectImmediateSubscription("LedOn", typeof(Bonsai.Reactive.PublishSubject), (value, ticks) =>
        {
            bonsaiThreadId = Thread.CurrentThread.ManagedThreadId;
            Interlocked.Exchange(ref immediateEmitTicks, Stopwatch.GetTimestamp() - ticks);
        });

        // Unity -> Bonsai by subject: keys typed in the Game view are pushed into UnityKey, the workflow
        // forwards them to KeyEcho, and they come back here.
        Workflow.SendToSubject(keySignal, "UnityKey");
        Workflow.InjectSubscription("KeyEcho", typeof(Bonsai.Reactive.PublishSubject), value => Log($"KeyEcho <- Bonsai: '{value}'"));

        // Unity -> Bonsai through an externalized property: the slider sets the FloatProperty's Value
        Workflow.InjectInput(sliderSource, "SliderInput");
        Workflow.InjectSubscription("SliderEcho", typeof(Bonsai.Reactive.PublishSubject), value => Log($"SliderEcho <- Bonsai: {value:0.00}"));

        // Swap a node for a Unity source, inside a group: the Timer feeding Trigger becomes the Space bar
        Workflow.ReplaceSource("Bonsai.Reactive.Timer", triggerSignal, "Trigger", "TrialTrigger");
        Workflow.InjectSubscription("Trigger", typeof(Bonsai.Reactive.PublishSubject), value =>
        {
            triggerFlashUntil = Time.time + 0.15f;
            Log($"Trigger <- Bonsai: {value}");
        }, "TrialTrigger");
    }

    void Start()
    {
        // Reads the saved workflow, so it works before the workflow runs
        ledOnTime = Workflow.GetExternalizedTimeSpan("LedOnTime");
    }

    void Update()
    {
        immediateDelayMs = Interlocked.Read(ref immediateEmitTicks) * 1000.0 / Stopwatch.Frequency;
        SetColor(trigger, Time.time < triggerFlashUntil ? TriggerHit : TriggerIdle);

        // Send the slider only when it moves, so the log isn't flooded
        if (state == State.Running && !Mathf.Approximately(slider, sentSlider))
        {
            sentSlider = slider;
            sliderSource.Send(slider);
        }
    }

    void OnGUI()
    {
        // Keys come from IMGUI events so this works with either input backend
        var e = Event.current;
        if (e.type == EventType.KeyDown && state == State.Running)
        {
            if (e.keyCode == KeyCode.Space)
            {
                triggerSignal.Send(++triggerCount);
                e.Use();
            }
            else if (e.keyCode >= KeyCode.A && e.keyCode <= KeyCode.Z || e.keyCode >= KeyCode.Alpha0 && e.keyCode <= KeyCode.Alpha9)
            {
                string key = e.keyCode.ToString().Replace("Alpha", "");
                Log($"UnityKey -> Bonsai: '{key}'");
                keySignal.Send(key);
                e.Use();
            }
        }

        GUILayout.BeginArea(new Rect(10, 10, 420, Screen.height - 20), GUI.skin.box);
        GUILayout.Label($"<b>Bonsai hooks demo</b>   workflow: {state}", new GUIStyle(GUI.skin.label) { richText = true });

        GUILayout.BeginHorizontal();
        GUI.enabled = state == State.Held;
        if (GUILayout.Button("Start (ReleaseStart)"))
        {
            state = State.Running;
            Workflow.ReleaseStart(this);
            Log($"Started, blink period {BlinkPeriodSeconds} s (SetNodeProperty)");
        }
        GUI.enabled = state == State.Running;
        if (GUILayout.Button("Stop (StopWorkflow)"))
        {
            state = State.Stopped;
            Workflow.StopWorkflow();
            SetColor(led, LedOff);
            Log("Stopped. Exit and re-enter Play to run again.");
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Label($"LED on for {ledOnTime.TotalMilliseconds:0} ms each blink (GetExternalizedTimeSpan \"LedOnTime\")");
        GUILayout.Label($"LedOn delay: queued {queuedDelayMs:0.0} ms, immediate {immediateDelayMs:0.000} ms on thread {bonsaiThreadId}");

        GUILayout.Label($"Slider -> SliderInput (InjectInput): {slider:0.00}");
        slider = GUILayout.HorizontalSlider(slider, 0f, 1f);
        GUILayout.Space(6);
        GUILayout.Label("Type A-Z / 0-9: SendToSubject \"UnityKey\"\nSpace: replaces TrialTrigger's Timer (ReplaceSource)");

        logScroll = GUILayout.BeginScrollView(logScroll);
        for (int i = log.Count - 1; i >= 0; i--)
        {
            GUILayout.Label(log[i]);
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void Log(string line)
    {
        log.Add($"{Time.time,7:0.00}  {line}");
        if (log.Count > 100)
        {
            log.RemoveAt(0);
        }
        Debug.Log(line);
    }

    private static double MsSince(long ticks) => (Stopwatch.GetTimestamp() - ticks) * 1000.0 / Stopwatch.Frequency;

    private static void SetColor(Renderer renderer, Color color)
    {
        renderer.material.color = color;
        if (renderer.material.HasProperty("_EmissionColor"))
        {
            renderer.material.EnableKeyword("_EMISSION");
            renderer.material.SetColor("_EmissionColor", color * 0.6f);
        }
    }

    private void BuildScene()
    {
        led = MakeSphere("LED", new Vector3(1.0f, 0.4f, 0), LedOff);
        trigger = MakeSphere("Trigger", new Vector3(2.6f, 0.4f, 0), TriggerIdle);
    }

    private Renderer MakeSphere(string name, Vector3 position, Color color)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = name;
        sphere.transform.SetParent(transform, false);
        sphere.transform.position = position;
        var renderer = sphere.GetComponent<Renderer>();
        SetColor(renderer, color);
        return renderer;
    }

    /// <summary>
    /// A Unity-driven source for <see cref="BonsaiWorkflow.InjectInput{T}"/>. Same pattern as
    /// <c>InputInjector.IntegerSource</c>; values sent before the workflow subscribes are dropped.
    /// </summary>
    private class FloatSource : Source<float>
    {
        private event Action<float> Raised;

        public void Send(float value) => Raised?.Invoke(value);

        public override IObservable<float> Generate()
        {
            return Observable.Create<float>(observer =>
            {
                Action<float> handler = observer.OnNext;
                Raised += handler;
                return Disposable.Create(() => Raised -= handler);
            });
        }
    }
}
