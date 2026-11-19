using UnityEngine;
using UnityEngine.InputSystem;

public class LifecycleTest : MonoBehaviour
{
    public BonsaiWorkflow Workflow;

    void Awake()
    {
        Workflow.Started.AddListener(() => Debug.Log($"Started. IsRunning={Workflow.IsRunning}"));
        Workflow.Completed.AddListener(() => Debug.Log($"Completed. IsRunning={Workflow.IsRunning}"));
        Workflow.OnError.AddListener(node => Debug.Log($"OnError node='{node}'. IsRunning={Workflow.IsRunning}"));
        Workflow.InjectSubscription("Out", typeof(Bonsai.Reactive.PublishSubject), v => Debug.Log($"value {v}"));
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.sKey.wasPressedThisFrame) { Workflow.StartWorkflow(); Debug.Log($"StartWorkflow called. IsRunning={Workflow.IsRunning}"); }
        if (keyboard.rKey.wasPressedThisFrame) { Workflow.Restart(); Debug.Log($"Restart called. IsRunning={Workflow.IsRunning}"); }
        if (keyboard.xKey.wasPressedThisFrame) { Workflow.StopWorkflow(); Debug.Log($"StopWorkflow called. IsRunning={Workflow.IsRunning}"); }
    }
}
