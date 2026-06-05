using System;
using UnityEngine;
using UnityEngine.Serialization;

// ─────────────────────────────────────────────────────────────────────────────
// Enums
// ─────────────────────────────────────────────────────────────────────────────

public enum HitType
{
    None = 0,
    Slash = 1,
    Blunt = 2,
    Pierce = 3
}

public enum AttackMoveType
{
    Lunge     = 0,
    Dash      = 1,
    RushTrack = 2,
    None      = 3,
    Slam      = 4  // 공중에서 지면으로 급강하. distance = 하강 속도(m/s), 착지 순간 hitbox 발동.
}

public enum AttackShape
{
    Sphere = 0,
    Cone   = 1,
    Box    = 2
}

public enum KnockbackType
{
    Radial      = 0,
    Directional = 1
}

public enum AttackCueTrigger
{
    Release = 0,
    Hit = 1,
    End = 2
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 1 — Setup: 공격 발동 전 단계
// ─────────────────────────────────────────────────────────────────────────────

[Serializable]
public struct AttackAnimationData
{
    public string stateName;
    public float transition;
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 2 — Movement: 공격자 자신의 이동
// ─────────────────────────────────────────────────────────────────────────────

[Serializable]
public struct AttackLungeData
{
    public AttackMoveType moveType;
    public float distance;
    public float duration;
    public AnimationCurve speedCurve;
    public bool stopOnHit;
    [Tooltip("Slam 전용: 초기 하강 속도 (m/s). distance/duration은 수평 이동에 그대로 사용.")]
    [Min(0f)] public float slamDescentSpeed;
}

// attacker가 공격 중 자신을 수직으로 띄우는 데이터 (점프 공격). lunge(수평)과는 독립.
// 적을 띄우는 launch와도 다른 개념 — 이건 공격자 자신의 점프. 떨어지는 시간은 중력에 의해 자동 결정.
[Serializable]
public struct AttackJumpData
{
    public bool enabled;
    [Min(0f)] public float height;
    public bool suspendAtApex;
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3 — Hit Detection: 히트박스 판정
// ─────────────────────────────────────────────────────────────────────────────

[Serializable]
public struct AttackHitboxData
{
    [Range(0f, 1f)]
    public float timing;
    public float offset;
    [FormerlySerializedAs("height")]
    public float yOffset;
    [Min(0f)]
    public float verticalTolerance;
}

[Serializable]
public struct AttackRepeatData
{
    public bool enabled;
    [Min(0f)]
    public float interval;
    public bool hitSameTargetOnce;
    [Tooltip("틱 1회 발동 시 적중 0이면 공격 즉시 종료")]
    public bool cancelOnMiss;
}

[Serializable]
public struct AttackShapeData
{
    public AttackShape type;
    [Min(0f)]
    public float radius;
    [Range(1f, 360f)]
    public float angle;
    [Min(0f)]
    public float length;
    [Min(0f)]
    public float width;
}

// overrideHitResult = true일 때 AttackExtraHit에서 사용하는 독립 히트 결과.
// false면 SO_Attack_Data 최상위 damage/hitType/knockback/launch/down을 그대로 사용.
[Serializable]
public struct AttackExtraHitResult
{
    public float damage;
    public HitType hitType;
    public AttackKnockbackData knockback;
    public AttackLaunchData launch;
    public AttackDownData down;
}

// 추가 히트박스. hitbox.timing이 독립적이므로 시차 다단 히트에 사용 가능 (좌우 동시, 발+검, 다른 타이밍 후속타 등).
[Serializable]
public struct AttackExtraHit
{
    public AttackShapeData shape;
    public AttackHitboxData hitbox;
    public AttackRepeatData repeat;
    public AttackExtraHitResult hitResult;
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 4 — Hit Result: 적중 시 결과
// ─────────────────────────────────────────────────────────────────────────────

[Serializable]
public struct AttackKnockbackData
{
    public KnockbackType type;
    public float force;
    public float friction;
}

[Serializable]
public struct AttackHitstopData
{
    public float duration;
    public float timeScale;
}

[Serializable]
public struct AttackDownData
{
    public bool enabled;
    public float duration;
}

// 잡몹을 공중으로 시각적으로 띄우는 데이터. ECS 시뮬 좌표는 평면 유지, 비주얼 y만 포물선 운동.
// 폐기 후 실제 y축 물리로 승격하기 쉽도록 enabled 플래그로 단순 토글.
[Serializable]
public struct AttackLaunchData
{
    public bool enabled;
    [Min(0f)] public float height;
    [Tooltip("정점에서 낙하하지 않고 공중에 머무는 시간. 0이면 즉시 낙하.")]
    [Min(0f)] public float suspendDuration;
}

// 흡혈. 적중 시 attacker에게 데미지 비율만큼 회복을 돌려준다. maxPerHit으로 1회 회복량 상한.
[Serializable]
public struct AttackLifeStealData
{
    public bool enabled;
    [Range(0f, 1f)] public float ratio;
    [Min(0f)] public float maxPerHit;
}

[Serializable]
public struct AttackSuperArmorData
{
    [Min(0f)] public float value;
    [Min(0f)] public float breakPower;
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 5 — Global Effects: 화면/시간 전역 연출
// ─────────────────────────────────────────────────────────────────────────────

[Serializable]
public struct AttackCameraShakeData
{
    public bool enabled;
    [Min(0f)] public float amplitude;
    [Min(0f)] public float duration;
    [Min(0f)] public float frequency;
}

// 필살기 컷인용 월드 감속. hitstop과 달리 더 긴 시간/덜 강한 감속을 발동 시점에 적용.
// 실행 시 LoopManager.SetTimeScales(worldScale, 1f)로 플레이어는 정상, 월드만 감속.
[Serializable]
public struct AttackSlowMoData
{
    public bool enabled;
    [Range(0f, 1f)] public float worldScale;
    [Min(0f)] public float duration;
}

[Serializable]
public struct AttackCameraCueData
{
    public bool enabled;
    public AttackCueTrigger trigger;
    [Min(0f), Tooltip("0 = use the attack duration.")]
    public float duration;
    [Min(0f), Tooltip("0 = keep current FOV.")]
    public float fovOverride;
    [Min(0f), Tooltip("0 = keep current distance.")]
    public float distanceOverride;
    public float heightDelta;
    public float yawVelocity;
}

[Serializable]
public struct AttackReleaseEffectData
{
    public AttackCameraShakeData shake;
    public AttackSlowMoData slowMo;
}

[Serializable]
public struct AttackHitEffectData
{
    public AttackCameraShakeData shake;
    public AttackHitstopData hitstop;
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 6 — Alternative Delivery: 근접 hitbox 외 공격 메커니즘
// ─────────────────────────────────────────────────────────────────────────────

// 발사체. attacker 전방에서 prefab을 스폰해 직선 이동시키며, 접촉 시 SO_Attack_Data의 데미지/넉백을 그대로 사용.
// 실행은 SkillRunner가 spawner를 호출해 prefab을 띄우는 방식.
[Serializable]
public struct AttackProjectileData
{
    public bool enabled;
    [Tooltip("Addressable projectile prefab 주소")]
    public string prefabAddress;
    [Min(0f)] public float speed;
    [Min(0f)] public float maxDistance;
    [Min(0f)] public float lifetime;
    [Tooltip("attacker forward 방향 기준 스폰 오프셋")]
    public Vector3 spawnOffset;
}

// 지속 장판/소환. attacker가 지정 위치에 prefab을 스폰하고 일정 시간 유지. tickInterval마다 데미지 판정.
// 데미지/넉백은 SO_Attack_Data의 기본값. shape/hitbox도 공유.
[Serializable]
public struct AttackFieldData
{
    public bool enabled;
    [Tooltip("Addressable field prefab 주소")]
    public string prefabAddress;
    [Min(0f)] public float duration;
    [Min(0.01f)] public float tickInterval;
    [Tooltip("attacker 기준 스폰 오프셋 (forward 방향)")]
    public float forwardOffset;
    public bool followAttacker;
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 7 — Feedback: 시각/음향 피드백
// ─────────────────────────────────────────────────────────────────────────────

[Serializable]
public struct AttackFeedbackData
{
    [Header("Cast")]
    [Tooltip("공격 발동 시 스폰되는 Addressable VFX 주소")]
    public string castVfxAddress;
    [Range(0f, 1f)]
    public float castVfxTiming;
    [Tooltip("플레이어 위치 기준 로컬 오프셋 (플레이어 forward/right/up 기준)")]
    public Vector3 castVfxOffset;
    public Vector3 castVfxEuler;

    [Header("Swing")]
    [Tooltip("WeaponRoot 자식 TrailRenderer GameObject 이름 목록. 비어 있으면 트레일을 켜지 않음.")]
    public string[] swingTrailIds;
    public SfxType swingSfx;

    [Header("Timing")]
    [Tooltip("히트박스 발동 시점에 스폰되는 Addressable VFX 주소. 적중 여부 무관.")]
    public string timingVfxAddress;
    [Tooltip("히트박스 중심 기준 로컬 오프셋 (플레이어 forward/right/up 기준)")]
    public Vector3 timingVfxOffset;

    [Header("Hit")]
    [Tooltip("적 피격 시 피격 위치에 스폰되는 Addressable VFX 주소")]
    public string hitVfxAddress;
    public SfxType hitSfx;
}

// ─────────────────────────────────────────────────────────────────────────────
// SO_Attack_Data
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "SO_Attack_Data", menuName = "Game/Combat/Attack Data")]
public sealed class SO_Attack_Data : ScriptableObject, ISerializationCallbackReceiver
{
    // ── Phase 1: Setup ───────────────────────────────────────────────────────
    [Header("Setup")]
    [SerializeField] private AttackAnimationData animation = new()
    {
        stateName = "Attack0",
        transition = 0.05f
    };

    // ── Phase 2: Movement ────────────────────────────────────────────────────
    [Header("Movement")]
    [SerializeField] private AttackLungeData lunge = new()
    {
        moveType = AttackMoveType.Lunge,
        distance = 0.8f,
        duration = 0.12f
    };
    [SerializeField] private AttackJumpData jump = new()
    {
        enabled = false,
        height = 2f
    };

    // ── Phase 3: Hit Detection ───────────────────────────────────────────────
    [Header("Hit Detection")]
    [SerializeField] private float duration = 0.4f;
    [SerializeField] private AttackShapeData shape = new()
    {
        type = AttackShape.Sphere,
        radius = 1.5f,
        angle = 90f,
        length = 3f,
        width = 2f
    };
    [SerializeField] private AttackHitboxData hitbox = new()
    {
        timing = 0.4f,
        offset = 1.0f,
        yOffset = 0.8f,
        verticalTolerance = 1.5f
    };
    [SerializeField] private AttackRepeatData repeat = new()
    {
        interval = 0.05f,
        hitSameTargetOnce = true
    };
    [SerializeField] private AttackExtraHit[] additionalHits;

    // ── Phase 4: Hit Result ──────────────────────────────────────────────────
    [Header("Hit Result")]
    [SerializeField] private float damage = 10f;
    [SerializeField] private HitType hitType = HitType.Slash;
    [SerializeField] private AttackKnockbackData knockback = new()
    {
        force = 6f,
        friction = 12f
    };
    [SerializeField] private AttackLaunchData launch = new()
    {
        enabled = false,
        height = 1.5f
    };
    [SerializeField] private AttackDownData down = new()
    {
        enabled = false,
        duration = 0.5f
    };
    [SerializeField] private AttackLifeStealData lifeSteal = new()
    {
        enabled = false,
        ratio = 0.1f,
        maxPerHit = 0f
    };
    [SerializeField] private AttackSuperArmorData superArmor;

    [Header("Camera")]
    [SerializeField] private AttackCameraCueData cameraCue;

    // ── Phase 5: Global Effects ──────────────────────────────────────────────
    [Header("Global Effects")]
    [SerializeField] private AttackReleaseEffectData releaseEffects;
    [SerializeField] private AttackHitEffectData hitEffects;

    // ── Phase 6: Alternative Delivery ────────────────────────────────────────
    [Header("Alternative Delivery")]
    [SerializeField] private AttackProjectileData projectile = new()
    {
        enabled = false,
        speed = 15f,
        maxDistance = 12f,
        lifetime = 1.5f
    };
    [SerializeField] private AttackFieldData field = new()
    {
        enabled = false,
        duration = 3f,
        tickInterval = 0.5f,
        forwardOffset = 2f
    };

    // ── Phase 7: Feedback ────────────────────────────────────────────────────
    [Header("Feedback")]
    [SerializeField] private AttackFeedbackData feedback;

    // 레거시 필드 — ISerializationCallbackReceiver를 통해 feedback으로 1회 이관 후 비워짐
    [SerializeField, HideInInspector] private string hitVfxAddress;
    [SerializeField, HideInInspector] private SfxType hitSfx = SfxType.None;

    // ── Properties (필드와 동일한 순서) ─────────────────────────────────────
    // Setup
    public AttackAnimationData Animation => animation;
    // Movement
    public AttackLungeData Lunge => lunge;
    public AttackJumpData Jump => jump;
    // Hit Detection
    public float Duration => duration;
    public AttackShapeData Shape => shape;
    public AttackHitboxData Hitbox => hitbox;
    public AttackRepeatData Repeat => repeat;
    public AttackExtraHit[] AdditionalHits => additionalHits;
    // Hit Result
    public float Damage => damage;
    public HitType HitType => hitType;
    public AttackKnockbackData Knockback => knockback;
    public AttackHitstopData Hitstop => hitEffects.hitstop;
    public AttackDownData Down => down;
    public AttackLaunchData Launch => launch;
    public AttackLifeStealData LifeSteal => lifeSteal;
    public AttackSuperArmorData SuperArmorData => superArmor;
    public float SuperArmor => superArmor.value;
    public float SuperArmorBreak => superArmor.breakPower;
    // Camera
    public AttackCameraCueData CameraCue => cameraCue;
    // Global Effects
    public AttackReleaseEffectData ReleaseEffects => releaseEffects;
    public AttackHitEffectData HitEffects => hitEffects;
    public AttackCameraShakeData CameraShake => hitEffects.shake;
    public AttackSlowMoData SlowMo => releaseEffects.slowMo;
    // Alternative Delivery
    public AttackProjectileData Projectile => projectile;
    public AttackFieldData Field => field;
    // Feedback
    public AttackFeedbackData Feedback => feedback;
    public string HitVfxAddress => feedback.hitVfxAddress;
    public SfxType HitSfx => feedback.hitSfx;

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize()
    {
        // 레거시 top-level 필드에 값이 남아 있으면 feedback 구조체로 이관
        if (!string.IsNullOrEmpty(hitVfxAddress))
        {
            if (string.IsNullOrEmpty(feedback.hitVfxAddress))
                feedback.hitVfxAddress = hitVfxAddress;
            hitVfxAddress = null;
        }
        if (hitSfx != SfxType.None)
        {
            if (feedback.hitSfx == SfxType.None)
                feedback.hitSfx = hitSfx;
            hitSfx = SfxType.None;
        }
    }

}

// ─────────────────────────────────────────────────────────────────────────────
// AttackShapeUtility — 히트박스 도형 판정 헬퍼 (NavAttackResolveSystem과 로직 미러)
// ─────────────────────────────────────────────────────────────────────────────

public static class AttackShapeUtility
{
    public static Vector3 GetQueryCenter(Vector3 attackerPosition, Vector3 attackerForward, AttackHitboxData hitbox, AttackShapeData shape)
    {
        Vector3 forward = Flatten(attackerForward);
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        float forwardOffset = hitbox.offset;
        if (shape.type == AttackShape.Box)
            forwardOffset += shape.length * 0.5f;

        return attackerPosition + forward * forwardOffset + Vector3.up * hitbox.yOffset;
    }

    public static float GetQueryRadius(AttackHitboxData hitbox, AttackShapeData shape)
    {
        float planarRadius = GetPlanarQueryRadius(shape);
        float verticalTolerance = Mathf.Max(0f, hitbox.verticalTolerance);
        return Mathf.Sqrt(planarRadius * planarRadius + verticalTolerance * verticalTolerance);
    }

    public static float GetPlanarQueryRadius(AttackShapeData shape)
    {
        return shape.type switch
        {
            AttackShape.Cone => Mathf.Max(shape.radius, shape.length),
            AttackShape.Box => Mathf.Sqrt(shape.length * shape.length + shape.width * shape.width) * 0.5f,
            _ => Mathf.Max(0f, shape.radius)
        };
    }

    public static float GetPlanarReach(AttackShapeData shape)
    {
        return shape.type switch
        {
            AttackShape.Cone => Mathf.Max(shape.radius, shape.length),
            AttackShape.Box => shape.length,
            _ => Mathf.Max(0f, shape.radius)
        };
    }

    public static bool Contains(Vector3 attackerPosition, Vector3 attackerForward, Vector3 targetPosition, AttackHitboxData hitbox, AttackShapeData shape)
        => Contains(attackerPosition, attackerForward, targetPosition, 0f, hitbox, shape);

    public static bool Contains(Vector3 attackerPosition, Vector3 attackerForward, Vector3 targetPosition, float targetRadius, AttackHitboxData hitbox, AttackShapeData shape)
    {
        Vector3 forward = Flatten(attackerForward);
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();
        targetRadius = Mathf.Max(0f, targetRadius);

        return shape.type switch
        {
            AttackShape.Cone => ContainsCone(attackerPosition, forward, targetPosition, targetRadius, hitbox, shape),
            AttackShape.Box => ContainsBox(attackerPosition, forward, targetPosition, targetRadius, hitbox, shape),
            _ => ContainsSphere(attackerPosition, forward, targetPosition, targetRadius, hitbox, shape)
        };
    }

    private static bool ContainsCone(Vector3 attackerPosition, Vector3 forward, Vector3 targetPosition, float targetRadius, AttackHitboxData hitbox, AttackShapeData shape)
    {
        Vector3 origin = attackerPosition + forward * hitbox.offset + Vector3.up * hitbox.yOffset;
        if (!IsWithinVerticalTolerance(origin, targetPosition, targetRadius, hitbox))
            return false;

        Vector3 delta = Flatten(targetPosition - origin);
        float length = Mathf.Max(shape.radius, shape.length) + targetRadius;
        if (delta.sqrMagnitude > length * length)
            return false;
        if (delta.sqrMagnitude <= targetRadius * targetRadius)
            return true;

        float angle = Mathf.Clamp(shape.angle, 1f, 360f);
        if (angle >= 359.9f)
            return true;

        float distance = delta.magnitude;
        float expandedHalfAngle = angle * 0.5f + Mathf.Atan2(targetRadius, Mathf.Max(0.0001f, distance)) * Mathf.Rad2Deg;
        float dot = Vector3.Dot(delta / distance, forward);
        return dot >= Mathf.Cos(Mathf.Min(180f, expandedHalfAngle) * Mathf.Deg2Rad);
    }

    private static bool ContainsBox(Vector3 attackerPosition, Vector3 forward, Vector3 targetPosition, float targetRadius, AttackHitboxData hitbox, AttackShapeData shape)
    {
        float length = shape.length;
        float width  = shape.width;
        Vector3 center = attackerPosition + forward * (hitbox.offset + length * 0.5f) + Vector3.up * hitbox.yOffset;
        if (!IsWithinVerticalTolerance(center, targetPosition, targetRadius, hitbox))
            return false;

        Vector3 right = new Vector3(forward.z, 0f, -forward.x);
        Vector3 delta = Flatten(targetPosition - center);

        return Mathf.Abs(Vector3.Dot(delta, forward)) <= length * 0.5f + targetRadius
               && Mathf.Abs(Vector3.Dot(delta, right)) <= width * 0.5f + targetRadius;
    }

    private static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    private static bool ContainsSphere(Vector3 attackerPosition, Vector3 forward, Vector3 targetPosition, float targetRadius, AttackHitboxData hitbox, AttackShapeData shape)
    {
        Vector3 center = attackerPosition + forward * hitbox.offset + Vector3.up * hitbox.yOffset;
        if (!IsWithinVerticalTolerance(center, targetPosition, targetRadius, hitbox))
            return false;

        Vector3 delta = Flatten(targetPosition - center);
        float radius = Mathf.Max(0f, shape.radius) + targetRadius;
        return delta.sqrMagnitude <= radius * radius;
    }

    private static bool IsWithinVerticalTolerance(Vector3 origin, Vector3 targetPosition, float targetRadius, AttackHitboxData hitbox)
    {
        float verticalTolerance = Mathf.Max(0f, hitbox.verticalTolerance) + targetRadius;
        return Mathf.Abs(targetPosition.y - origin.y) <= verticalTolerance;
    }
}
