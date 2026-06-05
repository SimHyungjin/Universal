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
    [Tooltip("밟고 넘을 수 있는 최대 장애물 높이. 이 값 이하의 장애물은 우회하지 않고 밟고 지나간다(0이면 모두 우회).")]
    [SerializeField, Min(0f)] private float stepHeight;

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
    public float StepHeight => stepHeight;
    public SO_Attack_Data EnemyAttack => enemyAttack;
    public float SectorPower => sectorPower;
}
