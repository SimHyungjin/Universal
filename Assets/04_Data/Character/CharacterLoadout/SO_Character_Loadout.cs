using UnityEngine;

[CreateAssetMenu(fileName = "SO_Character_Loadout", menuName = "Game/Character/Character Loadout")]
public sealed class SO_Character_Loadout : ScriptableObject
{
    [Header("Equipped Combat")]
    [SerializeField] private SO_Attack_ComboData equippedAttackCombo;
    [SerializeField] private SO_Skill_Data[] equippedSkills;

    public SO_Attack_ComboData EquippedAttackCombo => equippedAttackCombo;
    public SO_Skill_Data[] EquippedSkills => equippedSkills;
}
