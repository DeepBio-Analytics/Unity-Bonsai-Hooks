/*
    Sample: Unity to Bonsai and back. Feeds a frame counter into the IntInput externalized property
    of input-test.bonsai, and logs what the workflow's IntValue subject emits. Used by InputTest.
*/

using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Bonsai;
using UnityEngine;

/// <summary>
/// Replaces the <c>IntInput</c> externalized mapping in the workflow on <see cref="Workflow"/> with
/// <see cref="IntegerSource"/>, pushes the frame count into it every <c>Update</c>, and logs the values
/// that come back out of <c>IntValue</c>.
/// </summary>
public class InputInjector : MonoBehaviour
{
    public BonsaiWorkflow Workflow;

    private IntegerSource Source;
    private DataDevice Device;
    private int counter;

    void Awake()
    {
        // Play mode may not reload the domain or scene, so reset runtime state here
        counter = 0;
        Device = new DataDevice();
        Source = new IntegerSource { Device = Device };

        // Registered in Awake, before BonsaiWorkflow.Start builds the workflow. InjectInput resolves the
        // mapping's name when the workflow starts, so it doesn't matter which component's Awake runs first.
        Workflow.InjectInput(Source, "IntInput");
        Workflow.InjectSubscription("IntValue", typeof(Bonsai.Reactive.PublishSubject), value => { Debug.Log(value); });
    }

    void Update()
    {
        Device.Raise(counter);
        counter++;
    }

    /// <summary>
    /// A Bonsai source driven from Unity: each call to <see cref="DataDevice.Raise"/> becomes one value in
    /// the workflow. Values raised before the workflow subscribes are dropped. The same pattern works for
    /// <see cref="BonsaiWorkflow.ReplaceSource{T}"/>.
    /// </summary>
    class IntegerSource : Source<int>
    {
        public DataDevice Device;

        public override IObservable<int> Generate()
        {
            return Observable.Create<int>(observer =>
            {
                // Raise is called from Update, so the workflow receives each value on Unity's main thread
                Action<int> handler = observer.OnNext;
                Device.Raised += handler;
                return Disposable.Create(() => Device.Raised -= handler);
            });
        }
    }

    class DataDevice
    {
        public event Action<int> Raised;

        public void Raise(int number)
        {
            Raised?.Invoke(number);
        }
    }
}
