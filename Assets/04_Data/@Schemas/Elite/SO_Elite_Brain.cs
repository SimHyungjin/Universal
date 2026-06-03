using UnityEngine;

// 장수 전용 "비전투" 파라미터(AI 행동 + nav + 필드/매크로 이동).
// 전투 스탯(HP/방어/공격력/이동속도)은 SO_Character_Stats(공용)에서 오므로 여기 두지 않는다.
// 엘리트 = 캐릭터(SO_Character_Data) + 이 AI 파라미터.
[CreateAssetMenu(fileName = "SO_Elite_Brain", menuName = "Game/Elite/Elite Brain")]
public sealed class SO_Elite_Brain : ScriptableObject
{
    [Header("Nav (실체화 이동 — MapNavMonoAgent steer-only)")]
    [SerializeField] private float agentRadius = 0.35f;
    [SerializeField] private float stopDistance = 0.08f;

    [Header("AI")]
    [Tooltip("이 거리 안의 적을 우선 찾고, 없으면 거리 제한 없이 한 번 더 찾는다.")]
    [SerializeField] private float aggroRange = 18f;
    [SerializeField] private float attackRangePadding = 0.25f;
    [SerializeField] private float attackCooldown = 1.2f;
    [SerializeField] private float targetRefreshInterval = 0.2f;

    [Header("Boss Brain")]
    [Tooltip("사거리 대비 선호 교전 거리. 1보다 크면 살짝 밖에서 압박하다가 진입한다.")]
    [SerializeField, Min(0.1f)] private float preferredRangeRatio = 1.15f;
    [Tooltip("사거리 대비 너무 가까운 거리. 이 안으로 들어오면 후퇴/횡이동/회피를 섞는다.")]
    [SerializeField, Min(0.05f)] private float closeRangeRatio = 0.45f;
    [SerializeField, Min(0.05f)] private float skillThinkInterval = 0.35f;
    [SerializeField, Min(0.05f)] private float comboPressInterval = 0.14f;
    [SerializeField, Min(0.1f)] private float strafeInterval = 1.2f;
    [SerializeField, Range(0f, 1f)] private float lowHealthAggressionThreshold = 0.55f;
    [SerializeField, Range(0f, 1f)] private float criticalHealthAggressionThreshold = 0.25f;

    [Header("Field (비실체 매크로 이동)")]
    [SerializeField] private float fieldMoveSpeed = 35f;
    [SerializeField] private float fieldThinkInterval = 5f;

    public float AgentRadius => agentRadius;
    public float StopDistance => stopDistance;
    public float AggroRange => aggroRange;
    public float AttackRangePadding => attackRangePadding;
    public float AttackCooldown => attackCooldown;
    public float TargetRefreshInterval => targetRefreshInterval;
    public float PreferredRangeRatio => preferredRangeRatio;
    public float CloseRangeRatio => closeRangeRatio;
    public float SkillThinkInterval => skillThinkInterval;
    public float ComboPressInterval => comboPressInterval;
    public float StrafeInterval => strafeInterval;
    public float LowHealthAggressionThreshold => lowHealthAggressionThreshold;
    public float CriticalHealthAggressionThreshold => criticalHealthAggressionThreshold;
    public float FieldMoveSpeed => fieldMoveSpeed;
    public float FieldThinkInterval => fieldThinkInterval;
}
