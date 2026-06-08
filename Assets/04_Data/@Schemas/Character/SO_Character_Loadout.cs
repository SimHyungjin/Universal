using UnityEngine;

[CreateAssetMenu(fileName = "SO_Character_Loadout", menuName = "Game/Character/Character Loadout")]
public sealed class SO_Character_Loadout : ScriptableObject
{
    [Header("Equipped Combat")]
    [SerializeField] private SO_Attack_ComboData equippedAttackCombo;
    [Tooltip("퍼펙트 닷지 직후 반격창에서 공격 버튼이 트리거하는 반격기. 비우면 기본 콤보 1타로 폴백.")]
    [SerializeField] private SO_Attack_Data counterAttack;
    [SerializeField] private SO_Skill_Data[] equippedSkills;


    public SO_Attack_ComboData EquippedAttackCombo => equippedAttackCombo;
    public SO_Skill_Data[] EquippedSkills => equippedSkills;
    public SO_Attack_Data CounterAttack => counterAttack;
}
