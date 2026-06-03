using UnityEngine;

[CreateAssetMenu(fileName = "SO_Character_DashRule", menuName = "Game/Character/Dash Rule")]
public sealed class SO_Character_DashRule : ScriptableObject
{
    [Header("Timing")]
    [SerializeField] private float dashDuration = 0.16f;
    [SerializeField] private float dashCooldown = 2f;
    [SerializeField] private float dashInvincibleDuration = 0.12f;

    [Header("Speed")]
    [Tooltip("dashSpeed = moveSpeed × dashSpeedMultiplier (baseline: 5×5=25 m/s) → dashDistance = dashSpeed × dashDuration (5×5×0.16=4)")]
    [SerializeField] private float dashSpeedMultiplier = 5f;

    [Header("Gate Transition")]
    [Tooltip("Gate transition speed in meters per second. Duration is calculated as distance / speed.")]
    [SerializeField] private float gateTransitionSpeed = 18f;
    [Tooltip("How long after a dash ends the player can still enter a sector gate.")]
    [SerializeField] private float gateEntryGraceDuration = 0.35f;

    public float DashDuration => dashDuration;
    public float DashCooldown => dashCooldown;
    public float DashInvincibleDuration => dashInvincibleDuration;
    public float DashSpeedMultiplier => dashSpeedMultiplier;
    public float GateTransitionSpeed => gateTransitionSpeed;
    public float GateEntryGraceDuration => gateEntryGraceDuration;
}
