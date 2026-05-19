using UnityEngine;

[CreateAssetMenu(fileName = "SO_PlayerAnimationData", menuName = "Game/Player/Animation Data")]
public sealed class SO_PlayerAnimationData : ScriptableObject
{
    [Header("Locomotion")]
    [SerializeField] private string idleStateName = "Idle";
    [SerializeField] private string runStateName = "Run";
    [SerializeField] private float startRunTransition = 0.05f;
    [SerializeField] private float stopRunTransition = 0.18f;
    [SerializeField] private float runEnterDelay = 0.06f;
    [SerializeField] private float minRunDuration = 0.18f;
    [SerializeField] private float moveThreshold = 0.01f;

    [Header("Action States")]
    [SerializeField] private string jumpStartStateName = "";
    [SerializeField] private string jumpIdleStateName = "";
    [SerializeField] private string jumpEndStateName = "";
    [SerializeField] private string dashStateName = "";
    [SerializeField] private string hitStateName = "";
    [SerializeField] private string downStateName = "";
    [SerializeField] private string wakeupStateName = "";
    [SerializeField] private float actionTransition = 0.05f;

    public string IdleStateName => idleStateName;
    public string RunStateName => runStateName;
    public float StartRunTransition => startRunTransition;
    public float StopRunTransition => stopRunTransition;
    public float RunEnterDelay => runEnterDelay;
    public float MinRunDuration => minRunDuration;
    public float MoveThreshold => moveThreshold;
    public string JumpStartStateName => jumpStartStateName;
    public string JumpIdleStateName => jumpIdleStateName;
    public string JumpEndStateName => jumpEndStateName;
    public string DashStateName => dashStateName;
    public string HitStateName => hitStateName;
    public string DownStateName => downStateName;
    public string WakeupStateName => wakeupStateName;
    public float ActionTransition => actionTransition;
}
