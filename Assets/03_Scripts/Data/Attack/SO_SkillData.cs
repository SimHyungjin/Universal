using UnityEngine;

public enum SkillCategory
{
    Active   = 0,
    Ultimate = 1
}

[CreateAssetMenu(fileName = "SO_SkillData", menuName = "Game/Combat/Skill Data")]
public sealed class SO_SkillData : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string skillId;
    [SerializeField] private string displayName;
    [SerializeField] private SkillCategory category = SkillCategory.Active;

    [Header("UI")]
    [SerializeField] private Sprite icon;
    [SerializeField, TextArea(2, 5)] private string description;

    [Header("Activation")]
    [SerializeField, Min(0f)] private float cooldown = 5f;
    [SerializeField, Min(0f)] private float resourceCost;

    [Header("Attack")]
    [SerializeField] private SO_AttackData[] attackSequence;

    public string SkillId => skillId;
    public string DisplayName => displayName;
    public SkillCategory Category => category;
    public Sprite Icon => icon;
    public string Description => description;
    public float Cooldown => cooldown;
    public float ResourceCost => resourceCost;
    public SO_AttackData[] AttackSequence => attackSequence;
}
