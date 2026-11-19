/*
    The Unity-to-Bonsai half of the bridge: a source Unity fires by hand.

    Bonsai owns the experiment, so Unity never decides anything with one of these. It only reports: a key the
    operator pressed, a cue that started, a value the workflow asked to be told about. What the workflow does with
    it is the workflow's business.

    A signal reaches the workflow in one of two ways, both registered in Awake (before the workflow starts):

      BonsaiWorkflow.SendToSubject   pushes each value into a subject the workflow declares by name. This is the
                                     general route: add a typed subject to the workflow, subscribe to it wherever
                                     the value is needed, and the workflow still opens and runs in Bonsai alone
                                     (the subject is simply silent there).

      BonsaiWorkflow.ReplaceSource   stands in for a node that can't work inside Unity. Ctrl+J and Ctrl+E use it
                                     for Bonsai's KeyDown, which needs a Windows keyboard hook Unity doesn't give it.

    The other direction is BonsaiWorkflow.InjectSubscription and InjectImmediateSubscription.
*/

using System;
using System.Reactive.Subjects;
using Bonsai;

/// <summary>
/// A Bonsai source whose values come from Unity. Create one as a field, hand it to
/// <see cref="BonsaiWorkflow.SendToSubject{T}"/> or <see cref="BonsaiWorkflow.ReplaceSource{T}"/> in <c>Awake</c>,
/// and call <see cref="Send"/> whenever there is something to report.
/// </summary>
/// <typeparam name="T">The value's type. It must be the type the workflow's subject or downstream node expects.</typeparam>
public sealed class UnitySignal<T> : Source<T>
{
    private readonly Subject<T> subject = new Subject<T>();
    private readonly object gate = new object();

    /// <summary>
    /// Sends one value into the workflow, now, on the calling thread: there is no queue and no frame of delay.
    /// Safe to call from any thread, including the audio thread (it allocates nothing itself). The workflow's
    /// downstream nodes run on the caller's thread until the workflow moves the value elsewhere, so a workflow
    /// that does slow work on a signal sent from the main thread should start with an <c>ObserveOn</c>.
    /// </summary>
    /// <param name="value">The value the workflow receives.</param>
    /// <returns>The <see cref="BonsaiClock"/> reading taken just before the value was handed over, for logging.</returns>
    public long Send(T value)
    {
        // Rx subjects expect one OnNext at a time; the lock keeps that true when two threads send at once.
        lock (gate)
        {
            long sent = BonsaiClock.Now();
            subject.OnNext(value);
            return sent;
        }
    }

    public override IObservable<T> Generate()
    {
        return subject;
    }
}
