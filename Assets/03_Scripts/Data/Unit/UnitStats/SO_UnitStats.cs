using UnityEngine;

[CreateAssetMenu(fileName = "SO_UnitStats", menuName = "Game/Unit/Unit Stats")]
public sealed class SO_UnitStats : ScriptableObject
{
    [Header("Vitals")]
    [SerializeField] private float maxHealth = 30f;

    [Header("Combat")]
    [Tooltip("공격력 % 보너스. final = baseDamage × (1 + attackPower / 100)")]
    [SerializeField] private float attackPower;
    [Tooltip("방어력 % 감소. taken = incoming × (1 - defense / 100)")]
    [SerializeField] private float defense;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float agentRadius = 0.35f;
    [SerializeField] private float stopDistance = 0.08f;

    [Header("Attack")]
    [SerializeField] private SO_AttackData enemyAttack;

    public float MaxHealth => maxHealth;
    public float AttackPower => attackPower;
    public float Defense => defense;
    public float MoveSpeed => moveSpeed;
    public float AgentRadius => agentRadius;
    public float StopDistance => stopDistance;
    public SO_AttackData EnemyAttack => enemyAttack;
}
