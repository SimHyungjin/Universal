using UnityEngine;

[CreateAssetMenu(fileName = "SO_ActionRecovery", menuName = "Game/Common/Action Recovery")]
public sealed class SO_ActionRecovery : ScriptableObject
{
    [Header("Wakeup")]
    [SerializeField] private float wakeupDuration;
    [SerializeField] private float wakeupInvincibleDuration;

    public float WakeupDuration => wakeupDuration;
    public float WakeupInvincibleDuration => wakeupInvincibleDuration;
}
