using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "SO_Actor_AnimationData", menuName = "Game/Common/Actor Animation Data")]
public class SO_Actor_AnimationData : ScriptableObject
{
    [Header("Locomotion")]
    [SerializeField] protected string idleStateName = "Idle";
    [SerializeField] protected string runStateName = "Run";
    [SerializeField] protected float startRunTransition = 0.05f;
    [SerializeField] protected float stopRunTransition = 0.18f;
    [SerializeField] protected float runEnterDelay = 0.06f;
    [SerializeField] protected float minRunDuration = 0.18f;
    [SerializeField] protected float moveThreshold = 0.01f;

    [Header("Action States")]
    [SerializeField] protected string jumpStartStateName = "";
    [SerializeField] protected string jumpIdleStateName = "";
    [SerializeField] protected string jumpEndStateName = "";
    [SerializeField] protected string dashStateName = "";

    [Header("Hit Reaction States")]
    [FormerlySerializedAs("hitStateName")]
    [FormerlySerializedAs("lightStateName")]
    [SerializeField] protected string lightHitStateName = "Hit_0";

    [FormerlySerializedAs("heavyHitStateName")]
    [FormerlySerializedAs("heavyStateName")]
    [SerializeField] protected string heavyHitStateName = "";

    [SerializeField] protected string launchStateName = "";
    [SerializeField] protected string downStateName = "";
    [SerializeField] protected string wakeupStateName = "";
    [SerializeField] protected string deathStateName = "Death";

    [FormerlySerializedAs("actionTransition")]
    [FormerlySerializedAs("hitTransition")]
    [SerializeField] protected float reactionTransition = 0.05f;

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

    public string LightHitStateName => lightHitStateName;
    public string HeavyHitStateName => heavyHitStateName;
    public string LaunchStateName => launchStateName;
    public string DownStateName => downStateName;
    public string WakeupStateName => wakeupStateName;
    public string DeathStateName => deathStateName;
    public float ReactionTransition => reactionTransition;

    public string HitStateName => lightHitStateName;
    public string LightStateName => lightHitStateName;
    public string HeavyStateName => heavyHitStateName;
    public float ActionTransition => reactionTransition;
    public float HitTransition => reactionTransition;

    public HitReactionAnimSet BuildHitReactionSet()
        => new(lightHitStateName, heavyHitStateName, downStateName, launchStateName, wakeupStateName, deathStateName, reactionTransition);
}
