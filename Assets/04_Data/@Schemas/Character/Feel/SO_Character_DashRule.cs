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

    [Header("Perfect Dodge / Counter")]
    [Tooltip("닷지 시작 후 이 시간(초) 안에 적 타격이 닿으면 퍼펙트 닷지(데미지 무효 + 반격창).")]
    [SerializeField] private float perfectDodgeWindow = 0.15f;
    [Tooltip("퍼펙트 닷지 성공 후 공격 버튼이 '반격기'가 되는 창(초).")]
    [SerializeField] private float counterWindow = 0.6f;
    [Tooltip("퍼펙트 닷지 성공 시 추가 무적(초).")]
    [SerializeField] private float perfectDodgeInvincibleDuration = 0.2f;
    [Range(0f, 1f)]
    [Tooltip("퍼펙트 닷지 슬로모 게임 속도(0~1).")]
    [SerializeField] private float perfectDodgeTimeScale = 0.4f;
    [Tooltip("퍼펙트 닷지 슬로모 실시간 길이(초).")]
    [SerializeField] private float perfectDodgeSlowMoDuration = 0.3f;

    public float DashDuration => dashDuration;
    public float DashCooldown => dashCooldown;
    public float DashInvincibleDuration => dashInvincibleDuration;
    public float DashSpeedMultiplier => dashSpeedMultiplier;
    public float GateTransitionSpeed => gateTransitionSpeed;
    public float GateEntryGraceDuration => gateEntryGraceDuration;
    public float GatePostTransitionInvincibleDuration => gatePostTransitionInvincibleDuration;
    public float PerfectDodgeWindow => perfectDodgeWindow;
    public float CounterWindow => counterWindow;
    public float PerfectDodgeInvincibleDuration => perfectDodgeInvincibleDuration;
    public float PerfectDodgeTimeScale => perfectDodgeTimeScale;
    public float PerfectDodgeSlowMoDuration => perfectDodgeSlowMoDuration;
}
