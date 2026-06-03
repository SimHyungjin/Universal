using UnityEngine;

[CreateAssetMenu(fileName = "SO_Unit_Stats", menuName = "Game/Unit/Unit Stats")]
public sealed class SO_Unit_Stats : ScriptableObject
{
    [Header("Vitals")]
    [SerializeField] private float maxHealth = 30f;

    [Header("Combat")]
    [Tooltip("Attack bonus percent. final = baseDamage * (1 + attackPower / 100)")]
    [SerializeField] private float attackPower;
    [Tooltip("Defense percent reduction. taken = incoming * (1 - defense / 100)")]
    [SerializeField] private float defense;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float agentRadius = 0.35f;
    [SerializeField] private float stopDistance = 0.08f;

    [Header("Attack")]
    [SerializeField] private SO_Attack_Data enemyAttack;

    [Header("Sector Battle")]
    [Tooltip("Background sector-battle influence contributed by one unit of this type.")]
    [SerializeField, Min(0f)] private float sectorPower = 1f;

    public float MaxHealth => maxHealth;
    public float AttackPower => attackPower;
    public float Defense => defense;
    public float MoveSpeed => moveSpeed;
    public float AgentRadius => agentRadius;
    public float StopDistance => stopDistance;
    public SO_Attack_Data EnemyAttack => enemyAttack;
    public float SectorPower => sectorPower;
}
