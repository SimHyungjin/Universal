using System;
using UnityEngine;

public enum HitType
{
    None = 0,
    Slash = 1,
    Blunt = 2,
    Pierce = 3
}

[Serializable]
public struct AttackAnimationData
{
    public string stateName;
    public float transition;
}

[Serializable]
public struct AttackLungeData
{
    public float distance;
    public float duration;
}

[Serializable]
public struct AttackHitboxData
{
    [Range(0f, 1f)]
    public float timing;
    public float radius;
    public float offset;
    public float height;
}

public enum KnockbackType
{
    Radial      = 0,
    Directional = 1
}

[Serializable]
public struct AttackKnockbackData
{
    public KnockbackType type;
    public float force;
    public float duration;
    public float friction;
}

[Serializable]
public struct AttackHitstunData
{
    public float duration;
}

[Serializable]
public struct AttackHitstopData
{
    public float duration;
    public float timeScale;
}

[CreateAssetMenu(fileName = "SO_AttackData", menuName = "Game/Combat/Attack Data")]
public sealed class SO_AttackData : ScriptableObject
{
    [Header("Animation")]
    [SerializeField] private AttackAnimationData animation = new()
    {
        stateName = "Attack0",
        transition = 0.05f
    };

    [Header("Timing")]
    [SerializeField] private float duration = 0.4f;
    [SerializeField] private AttackLungeData lunge = new()
    {
        distance = 0.8f,
        duration = 0.12f
    };
    [SerializeField] private AttackHitboxData hitbox = new()
    {
        timing = 0.4f,
        radius = 1.5f,
        offset = 1.0f,
        height = 0.8f
    };

    [Header("Hit Result")]
    [SerializeField] private float damage = 10f;
    [SerializeField] private AttackKnockbackData knockback = new()
    {
        force = 6f,
        duration = 0.3f,
        friction = 12f
    };
    [SerializeField] private AttackHitstunData hitstun = new()
    {
        duration = 0.5f
    };
    [SerializeField] private AttackHitstopData hitstop = new()
    {
        duration = 0.08f,
        timeScale = 0.02f
    };
    [SerializeField] private float superArmorBreak;
    [SerializeField] private HitType hitType = HitType.Slash;

    [Header("Feedback")]
    [SerializeField] private string hitVfxAddress;
    [SerializeField] private SfxType hitSfx = SfxType.None;

    public AttackAnimationData Animation => animation;
    public float Duration => duration;
    public AttackLungeData Lunge => lunge;
    public AttackHitboxData Hitbox => hitbox;
    public float Damage => damage;
    public AttackKnockbackData Knockback => knockback;
    public AttackHitstunData Hitstun => hitstun;
    public AttackHitstopData Hitstop => hitstop;
    public float SuperArmorBreak => superArmorBreak;
    public HitType HitType => hitType;
    public string HitVfxAddress => hitVfxAddress;
    public SfxType HitSfx => hitSfx;
}
