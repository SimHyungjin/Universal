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
    [Tooltip("게이트 통과가 끝난 직후 캐릭터가 무적인 시간(초). 전환 도중엔 이미 피격 무시이고, 이 값은 도착 섹터에서 잡몹 떼에 둘러싸여 즉사하는 것을 막는다.")]
    [SerializeField] private float gatePostTransitionInvincibleDuration = 1f;

    public float DashDuration => dashDuration;
    public float DashCooldown => dashCooldown;
    public float DashInvincibleDuration => dashInvincibleDuration;
    public float DashSpeedMultiplier => dashSpeedMultiplier;
    public float GateTransitionSpeed => gateTransitionSpeed;
    public float GateEntryGraceDuration => gateEntryGraceDuration;
    public float GatePostTransitionInvincibleDuration => gatePostTransitionInvincibleDuration;
}
