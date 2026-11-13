using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

/// <summary>
/// Runs actions queued from Bonsai's threads on Unity's main thread, at the start of <c>Update</c> and of <c>LateUpdate</c>.
/// Unity's API can only be called from the main thread, so every subscription injected through
/// <see cref="BonsaiWorkflow"/> is delivered through here.
/// </summary>
/// <remarks>
/// Creates its own <c>Dispatcher</c> GameObject before the first scene loads, marked
/// <c>DontDestroyOnLoad</c>, so it never appears in a scene's hierarchy and needs no setup.
/// Adapted from the unmaintained emotional-cities/unity package (https://github.com/emotional-cities/unity)
/// and fixed for Bonsai 2.9; see docs/developer/Bonsai-Internals.md.
/// </remarks>
[DefaultExecutionOrder(-32000)]
public class BonsaiDispatcher : MonoBehaviour
{
    static BonsaiDispatcher Instance;
    static volatile bool Queued = false;

    // Its own object, not the Backlog list: Drain swaps which list Backlog refers to, so a thread that locked
    // "Backlog" could be holding the list the main thread is now running through.
    static readonly object Gate = new object();
    static List<Action> Backlog = new List<Action>(8);
    static List<Action> Actions = new List<Action>(8);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        // Entering Play mode without a domain reload keeps static fields from the last session, including any
        // Bonsai events still queued when it ended, which would otherwise be delivered to the new one
        lock (Gate)
        {
            Backlog.Clear();
            Queued = false;
        }

        Actions.Clear();

        if (Instance == null)
        {
            Instance = new GameObject("Dispatcher").AddComponent<BonsaiDispatcher>();
            DontDestroyOnLoad(Instance.gameObject);
        }
    }

    /// <summary>Runs <paramref name="action"/> on a thread-pool thread. Unused by this project; kept from the upstream package.</summary>
    /// <param name="action">The work to run.</param>
    /// <param name="state">Passed to <paramref name="action"/>.</param>
    public static void RunAsync(Action<object> action, object state)
    {
        ThreadPool.QueueUserWorkItem(o => action(o), state);
    }

    /// <summary>
    /// Queues <paramref name="action"/> to run on Unity's main thread at the next <c>Update</c> or <c>LateUpdate</c>. Safe to
    /// call from any thread. Actions run in the order they were queued; one that throws is logged and the
    /// rest of that frame's batch still runs.
    /// </summary>
    /// <param name="action">The work to run on the main thread.</param>
    public static void RunOnMainThread(Action action)
    {
        lock (Gate)
        {
            Backlog.Add(action);
            Queued = true;
        }
    }

    // Twice a frame, before any other script (execution order -32000). The Update pass delivers what arrived
    // while the last frame was drawn. The LateUpdate pass delivers what arrived during this frame's Updates,
    // so that it is in place before the scripts that run late in the frame (the cue capture, the logger)
    // rather than waiting a whole frame.
    private void Update()
    {
        Drain();
    }

    private void LateUpdate()
    {
        Drain();
    }

    private void Drain()
    {
        if (!Queued)
        {
            return;
        }

        lock (Gate)
        {
            var tmp = Actions;
            Actions = Backlog;
            Backlog = tmp;
            Queued = false;
        }

        // Each action is caught on its own. An exception that escaped this loop used to skip the Clear
        // below, and the next swap then put the uncleared list back as the backlog: every event already
        // delivered ran again (a second TrialStarted, for example), and each later batch stopped at the
        // same failing action.
        foreach (var action in Actions)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        Actions.Clear();
    }
}
