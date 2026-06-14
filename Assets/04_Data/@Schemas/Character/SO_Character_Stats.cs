using UnityEngine;

// 모든 캐릭터(플레이어/장수)가 공유하는 전투 스탯. 바이탈/전투/이동/게이지.
// AI·필드 같은 비전투 파라미터는 여기 두지 않는다(SO_Character_Data.AiBrain = SO_Elite_Brain이 따로 갖는다).
[CreateAssetMenu(fileName = "SO_Character_Stats", menuName = "Game/Character/Character Stats")]
public sealed class SO_Character_Stats : ScriptableObject
{
    [Header("Vitals")]
    [SerializeField] private float maxHealth = 100f;

    [Header("Combat")]
    [Tooltip("공격력 % 보너스. final = baseDamage × (1 + attackPower / 100)")]
    [SerializeField] private float attackPower;
    [Tooltip("방어력 % 감소. taken = incoming × (1 - defense / 100)")]
    [SerializeField] private float defense;

    [Header("Break")]
    [Tooltip("0 이하면 브레이크 게이지를 사용하지 않는다. 장수/엘리트용 개인 전투 압박 게이지.")]
    [SerializeField, Min(0f)] private float breakMax;

    [Header("Movement")]
    [Tooltip("이동 속도 절대값 (m/s). 점프 높이/대시 거리는 이 값에 비례 계산됨")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("Body")]
    [Tooltip("잡몹이 못 들어오는 몸체 반경 + 잡몹 공격 적중 판정 반경(공용). 키우면 잡몹이 더 멀리서 둘러싸고 더 후하게 적중.")]
    [SerializeField, Min(0f)] private float bodyRadius = 0.5f;

    [Header("Gauge")]
    [SerializeField, Min(0f)] private float gaugeMax = 100f;
    [Tooltip("적에게 가한 데미지 1당 충전량")]
    [SerializeField, Min(0f)] private float gaugeGainPerDamage = 0.5f;
    [Tooltip("피격 1회당 충전량")]
    [SerializeField, Min(0f)] private float gaugeGainOnReceive = 15f;

    [Header("Sector Battle")]
    [Tooltip("Base influence used by background sector battles before loadout bonuses.")]
    [SerializeField, Min(0f)] private float baseSectorPower = 300f;

    public float MaxHealth => maxHealth;
    public float AttackPower => attackPower;
    public float Defense => defense;
    public float BreakMax => breakMax;
    public float MoveSpeed => moveSpeed;
    public float BodyRadius => bodyRadius;
    public float GaugeMax => gaugeMax;
    public float GaugeGainPerDamage => gaugeGainPerDamage;
    public float GaugeGainOnReceive => gaugeGainOnReceive;
    public float BaseSectorPower => baseSectorPower;
}
