using System;
using UnityEngine;

public enum SkillCategory
{
    Active   = 0,
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

[Serializable]
public struct SkillCutInData
{
    public bool enabled;
    [Min(0f), Tooltip("컷인 유지 시간 (이후 원래 값으로 복원)")]
    public float duration;
    [Min(0f), Tooltip("0 = 변경 없음")]
    public float fovOverride;
    [Min(0f), Tooltip("0 = 변경 없음")]
    public float distanceOverride;
    [Tooltip("카메라 높이 오프셋 (양수 = 위로)")]
    public float heightDelta;
    [Tooltip("cutIn 동안 카메라 선회 속도 (degrees/sec, 양수 = 우→좌)")]
    public float yawVelocity;
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
    [Tooltip("Ultimate 전용: 발동 시 무적 지속 시간")]
    [SerializeField, Min(0f)] private float invincibleDuration;

    [Header("Ultimate Overlay")]
    [SerializeField] private UltimateOverlayData overlay;

    [Header("Camera Cut-In")]
    [SerializeField] private SkillCutInData cutIn;

    [Header("Attack")]
    [SerializeField] private SO_AttackData[] attackSequence;

    public string SkillId => skillId;
    public string DisplayName => displayName;
    public SkillCategory Category => category;
    public Sprite Icon => icon;
    public string Description => description;
    public float Cooldown => cooldown;
    public float ResourceCost => resourceCost;
    public float InvincibleDuration => invincibleDuration;
    public UltimateOverlayData Overlay => overlay;
    public SkillCutInData CutIn => cutIn;
    public SO_AttackData[] AttackSequence => attackSequence;
}
