using UnityEngine;

[CreateAssetMenu(fileName = "SO_DashRule", menuName = "Game/Common/Dash Rule")]
public sealed class SO_DashRule : ScriptableObject
{
    [Header("Timing")]
    [SerializeField] private float dashDuration = 0.16f;
    [SerializeField] private float dashCooldown = 2f;
    [SerializeField] private float dashInvincibleDuration = 0.12f;

    [Header("Speed")]
    [Tooltip("dashSpeed = moveSpeed × dashSpeedMultiplier (baseline: 5×5=25 m/s) → dashDistance = dashSpeed × dashDuration (5×5×0.16=4)")]
    [SerializeField] private float dashSpeedMultiplier = 5f;

    public float DashDuration => dashDuration;
    public float DashCooldown => dashCooldown;
    public float DashInvincibleDuration => dashInvincibleDuration;
    public float DashSpeedMultiplier => dashSpeedMultiplier;
}
