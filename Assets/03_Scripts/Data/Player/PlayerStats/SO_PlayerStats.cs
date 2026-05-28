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

    public float MaxHealth => maxHealth;
    public float AttackPower => attackPower;
    public float Defense => defense;
    public float MoveSpeed => moveSpeed;
}
