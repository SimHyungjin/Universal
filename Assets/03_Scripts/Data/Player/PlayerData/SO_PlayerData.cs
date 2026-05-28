using UnityEngine;

[CreateAssetMenu(fileName = "SO_PlayerData", menuName = "Game/Player/PlayerData")]
public sealed class SO_PlayerData : ScriptableObject
{
    [Header("Identity (Per-Character)")]
    [SerializeField] private SO_PlayerStats statsData;
    [SerializeField] private SO_PlayerAnimationData animationData;
    [SerializeField] private SO_AttackComboData _attackCombo;
    [Tooltip("캐릭터가 들고 다니는 스킬 로드아웃. 4개 슬롯이 의도이며 전체 스킬 풀과는 별개.")]
    [SerializeField] private SO_SkillData[] skills;

    [Header("Common (Shared Across Characters)")]
    [SerializeField] private SO_LocomotionFeel locomotionFeel;
    [SerializeField] private SO_WorldPhysics worldPhysics;
    [SerializeField] private SO_InputBuffering inputBuffering;
    [SerializeField] private SO_JumpFeel jumpFeel;
    [SerializeField] private SO_DashRule dashRule;
    [SerializeField] private SO_ActionRecovery actionRecovery;

    public SO_PlayerStats StatsData => statsData;
    public SO_PlayerAnimationData AnimationData => animationData;
    public SO_AttackComboData AttackCombo => _attackCombo;
    public SO_SkillData[] Skills => skills;
    public SO_LocomotionFeel LocomotionFeel => locomotionFeel;
    public SO_WorldPhysics WorldPhysics => worldPhysics;
    public SO_InputBuffering InputBuffering => inputBuffering;
    public SO_JumpFeel JumpFeel => jumpFeel;
    public SO_DashRule DashRule => dashRule;
    public SO_ActionRecovery ActionRecovery => actionRecovery;
}
