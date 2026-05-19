using UnityEngine;

[CreateAssetMenu(fileName = "SO_PlayerActionData", menuName = "Game/Player/Action Data")]
public sealed class SO_PlayerActionData : ScriptableObject
{
    [Header("Hit Reaction")]
    [SerializeField] private float hitstunDuration = 0.35f;
    [SerializeField] private float knockbackFriction = 14f;
    [SerializeField] private float downKnockbackThreshold = 12f;
    [SerializeField] private float downDuration = 1f;
    [SerializeField] private float wakeupDuration = 0.65f;
    [SerializeField] private float wakeupInvincibleDuration = 0.45f;

    public float HitstunDuration => hitstunDuration;
    public float KnockbackFriction => knockbackFriction;
    public float DownKnockbackThreshold => downKnockbackThreshold;
    public float DownDuration => downDuration;
    public float WakeupDuration => wakeupDuration;
    public float WakeupInvincibleDuration => wakeupInvincibleDuration;
}
