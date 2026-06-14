using System;
using UnityEngine;

public enum SkillCategory
{
    Active = 0,
    Ultimate = 1
}

[Serializable]
public struct UltimateOverlayData
{
    public bool enabled;
    public Color flashColor;
    public Sprite portrait;
    [Min(0f)] public float fadeInDuration;
    [Min(0f)] public float holdDuration;
    [Min(0f)] public float fadeOutDuration;
}

// 공격 범위 예고(telegraph). 스킬 시퀀스 시작 앞에 leadTime만큼 예고 페이즈를 prepend한다.
// leadTime 동안 시퀀스(hitbox)는 시작하지 않으므로, 즉발 공격들로 짜인 시퀀스여도 피할 시간이 생긴다.
// 예고 범위는 attackSequence[0]의 Shape/Hitbox를 그대로 쓴다(첫타 모양만). 시전자는 예고 중 입력이 잠긴다.
[Serializable]
public struct AttackTelegraphData
{
    public bool enabled;
    [Min(0f), Tooltip("예고 표시 시간(초). 이 시간만큼 시퀀스 시작이 지연되고 시전자는 제자리에 고정된다.")]
    public float leadTime;
    [Tooltip("바닥 예고 데칼 색. leadTime이 끝나갈수록 진해지며 깜빡인다.")]
    public Color color;
}

[Serializable]
public struct SkillCutInData
{
    public bool enabled;
    [Min(0f), Tooltip("Cut-in duration. Camera values restore after this.")]
    public float duration;
    [Min(0f), Tooltip("0 = keep current FOV.")]
    public float fovOverride;
    [Min(0f), Tooltip("0 = keep current distance.")]
    public float distanceOverride;
    [Tooltip("Temporary camera height offset.")]
    public float heightDelta;
    [Tooltip("Camera yaw velocity during cut-in, in degrees per second.")]
    public float yawVelocity;
}

[CreateAssetMenu(fileName = "SO_Skill_Data", menuName = "Game/Combat/Skill Data")]
public sealed class SO_Skill_Data : ScriptableObject
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
    [Tooltip("Ultimate only: invincibility duration after activation.")]
    [SerializeField, Min(0f)] private float invincibleDuration;

    [Header("Ultimate Overlay")]
    [SerializeField] private UltimateOverlayData overlay;

    [Header("Telegraph")]
    [SerializeField] private AttackTelegraphData telegraph = new()
    {
        enabled = false,
        leadTime = 0.8f,
        color = new Color(1f, 0.2f, 0.2f, 1f)
    };

    [Header("Attack")]
    [SerializeField] private SO_Attack_Data[] attackSequence;
    [Tooltip("true면 각 타가 명중하지 않아도 다음 타로 진행한다(발사체/장판 등 비동기로 맞는 스킬용). false(기본)=명중해야 진행.")]
    [SerializeField] private bool advanceWithoutHit;

    [Header("Sector Battle")]
    [Tooltip("Additional background sector-battle influence granted while this skill is equipped.")]
    [SerializeField, Min(0f)] private float sectorPowerBonus;

    public string SkillId => skillId;
    public string DisplayName => displayName;
    public SkillCategory Category => category;
    public Sprite Icon => icon;
    public string Description => description;
    public float Cooldown => cooldown;
    public float ResourceCost => resourceCost;
    public float InvincibleDuration => invincibleDuration;
    public UltimateOverlayData Overlay => overlay;
    public AttackTelegraphData Telegraph => telegraph;
    public SO_Attack_Data[] AttackSequence => attackSequence;
    public bool AdvanceWithoutHit => advanceWithoutHit;
    public float SectorPowerBonus => sectorPowerBonus;
    public bool IsUltimate => category == SkillCategory.Ultimate;
    public bool HasAttackSequence => attackSequence != null && attackSequence.Length > 0 && attackSequence[0] != null;
}
