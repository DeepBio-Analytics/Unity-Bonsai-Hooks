/*
    Sample: Bonsai to Unity. Logs every value the TimerOutput subject in timer-workflow.bonsai
    emits, with the time it was emitted on Bonsai's thread. Used by SampleScene.
*/

using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Subscribes to the <c>TimerOutput</c> subject of the workflow on <see cref="BonsaiWorkflow"/> and logs
/// each value with its delivery delay (from Bonsai emitting it to Unity's main thread running the callback).
/// </summary>
public class WorkflowSubscriber : MonoBehaviour
{
    public BonsaiWorkflow BonsaiWorkflow;

    // Hooks are registered in Awake: the workflow starts in BonsaiWorkflow.Start, and Unity doesn't order
    // different components' Start calls.
    void Awake()
    {
        // The workflow has two nodes named TimerOutput (the PublishSubject and a SubscribeSubject), so the
        // exact type is what picks the node. Files saved by Bonsai 2.6/2.7 write the subject as
        // xsi:type="PublishSubject", which loads as the obsolete Bonsai.Expressions.PublishSubjectBuilder;
        // re-save them in a current Bonsai and refresh the asset.
        BonsaiWorkflow.InjectSubscription("TimerOutput", typeof(Bonsai.Reactive.PublishSubject), (value, timestamp) =>
        {
            double delayMs = (Stopwatch.GetTimestamp() - timestamp) * 1000.0 / Stopwatch.Frequency;
            Debug.Log($"TimerOutput {value} (delivered {delayMs:F1} ms after Bonsai emitted it)");
        });
    }
}
