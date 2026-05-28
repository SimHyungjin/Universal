using UnityEngine;

[CreateAssetMenu(fileName = "SO_PlayerStats", menuName = "Game/Player/Player Stats")]
public sealed class SO_PlayerStats : ScriptableObject
{
    [Header("Vitals")]
    [SerializeField] private float maxHealth = 100f;

    [Header("Combat")]
    [Tooltip("공격력 % 보너스. final = baseDamage × (1 + attackPower / 100)")]
    [SerializeField] private float attackPower;
    [Tooltip("방어력 % 감소. taken = incoming × (1 - defense / 100)")]
    [SerializeField] private float defense;

    [Header("Movement")]
    [Tooltip("이동 속도 절대값 (m/s). 점프 높이/대시 거리는 이 값에 비례 계산됨")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("Gauge")]
    [SerializeField, Min(0f)] private float gaugeMax = 100f;
    [Tooltip("적에게 가한 데미지 1당 충전량")]
    [SerializeField, Min(0f)] private float gaugeGainPerDamage = 0.5f;
    [Tooltip("피격 1회당 충전량")]
    [SerializeField, Min(0f)] private float gaugeGainOnReceive = 15f;

    public float MaxHealth => maxHealth;
    public float AttackPower => attackPower;
    public float Defense => defense;
    public float MoveSpeed => moveSpeed;
    public float GaugeMax => gaugeMax;
    public float GaugeGainPerDamage => gaugeGainPerDamage;
    public float GaugeGainOnReceive => gaugeGainOnReceive;
}
