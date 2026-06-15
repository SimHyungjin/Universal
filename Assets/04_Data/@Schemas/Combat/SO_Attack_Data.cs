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

// 장판(AttackFieldDelivery)이 스폰되는 위치 기준.
public enum FieldOrigin
{
    // attacker 전방으로 forwardOffset만큼 떨어진 고정 지점. (followAttacker로 추종 가능)
    ForwardOffset = 0,
    // 조준/전방 방향의 최근접 적 발밑(없으면 forwardOffset을 최대 사거리로 한 전방 끝점). 번개형.
    AimTarget = 1,
    // 발사체(Projectile)가 도착/적중해 소멸하는 위치. projectile.enabled와 함께 쓴다. 투척 폭발형.
    ProjectileImpact = 2
}

public enum CastVfxSpace
{
    World = 0,
    Actor = 1
}

public enum AttackAnimationTimingMode
{
    PlayAtNormalSpeed = 0,
    ScaleToAttackDuration = 1,
    UseFixedSpeed = 2
}

public enum AttackDeliveryType
{
    Melee = 0,
    Projectile = 1,
    Field = 2
}

public enum AttackProjectileAimMode
{
    Forward = 0,
    InputDirection = 1,
    AutoTarget = 2,
    NearestTarget = 3
}

public enum AttackHitDeduplication
{
    None = 0,
    OncePerDeliveryEvent = 1,
    OncePerAttack = 2
}

public enum AttackMovementType
{
    Lunge = 0,
    // 1, 2 = (구) Dash/RushTrack 제거됨 — 전진 이동은 Lunge(음수=후진)로 통합, 텔레그래프 레인은 repeat+이동으로 판정.
    SelfJump = 3,
    Suspend = 4,
    Slam = 5
}

public enum AttackFeedbackTrigger
{
    Timeline = 0,
    DeliveryFire = 1 // delivery의 히트박스가 발동하는 순간(명중 여부 무관). 명중 시 연출은 hitResult가 담당.
}

public enum AttackFeedbackVfxOrigin
{
    Actor = 0,
    WorldAtActor = 1,
    DeliveryCenter = 2
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 1 — Setup: 공격 발동 전 단계
// ─────────────────────────────────────────────────────────────────────────────

[Serializable]
public struct AttackAnimationData
{
    public string stateName;
    public float transition;
    public AttackAnimationTimingMode timingMode;
    [Min(0f)] public float speed;
    [Range(0f, 1f)] public float startNormalizedTime;
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 2 — Movement: 공격자 자신의 이동
// ─────────────────────────────────────────────────────────────────────────────

// 수평 전진 이동(lunge). distance를 duration 동안 speedCurve로 보간. 음수 distance=후진.
[Serializable]
public struct AttackLungeData
{
    public float distance;
    public float duration;
    public AnimationCurve speedCurve;
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3 — Hit Detection: 히트박스 판정 (shape/hitbox는 신규 delivery 이벤트가 공유)
// ─────────────────────────────────────────────────────────────────────────────

// 히트 볼륨의 배치. 크기(verticalTolerance 포함)는 AttackShapeData, 타이밍은 delivery.startTime이 담당.
[Serializable]
public struct AttackHitboxData
{
    public float offset;
    [FormerlySerializedAs("height")]
    public float yOffset;
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
    [Tooltip("히트 볼륨의 수직 반높이(±). 평면 footprint 밖 수직 허용 범위.")]
    [Min(0f)]
    public float verticalTolerance;
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

// 전역 게임속도를 timeScale로 duration(실시간)만큼 낮췄다 복원하는 동일 프리미티브.
// hitstop(짧은 프리즈)과 slowMo(긴 감속)가 같은 데이터를 공유 — 값(짧고 강하게 vs 길고 약하게)만 다르다.
// 활성 판정은 duration>0로 통일(별도 enabled 없음).
[Serializable]
public struct AttackTimeScaleData
{
    [FormerlySerializedAs("worldScale")]
    [Range(0f, 1f)] public float timeScale;
    [Min(0f)] public float duration;
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

// ─────────────────────────────────────────────────────────────────────────────
// Timeline Schema. Runtime consumes delivery/movement events and combo queue rules.
// Report validates reserved options that are not implemented yet.
// ─────────────────────────────────────────────────────────────────────────────

[Serializable]
public struct AttackHitRepeat
{
    public bool enabled;
    [Min(0f)] public float interval;
    [Tooltip("repeat 발동 창(초). startTime부터 이 시간 동안 interval마다 발동. maxCount로 횟수 상한.")]
    [Min(0f)] public float duration;
    [Min(0)] public int maxCount;
}

[Serializable]
public struct AttackMeleeDelivery
{
    public AttackShapeData shape;
    public AttackHitboxData hitbox;
    public AttackHitRepeat repeat;
    // 흐름 정책(melee 전용): 명중 시 이동 정지 / 창 끝까지 무적중 시 공격 조기 종료.
    public AttackDeliveryFlow flow;
}

[Serializable]
public struct AttackProjectileDelivery
{
    public string prefabAddress;
    public AttackShapeData shape;
    public AttackHitboxData hitbox;
    public Vector3 spawnOffset;
    [Min(0f)] public float speed;
    [Min(0f)] public float maxDistance;
    [Min(0f)] public float lifetime;
    public bool pierce;
    [Min(1)] public int count;
    [Range(0f, 360f)] public float spreadAngle;
    public AttackProjectileAimMode aimMode;
}

[Serializable]
public struct AttackFieldDelivery
{
    public string prefabAddress;
    public AttackShapeData shape;
    public AttackHitboxData hitbox;
    public FieldOrigin origin;
    [Tooltip("장판 지속 시간(초).")]
    [Min(0f)] public float lifetime;
    [Min(0.01f)] public float tickInterval;
    public float forwardOffset;
    public bool followAttacker;
}

[Serializable]
public struct AttackHitResultData
{
    public float damage;
    [Tooltip("슈퍼아머 관통(인터럽트) 임계. 상대 슈퍼아머 value보다 크면 행동을 끊는다.")]
    [FormerlySerializedAs("breakDamage")]
    [Min(0f)] public float superArmorBreak;
    [Tooltip("그로기(break) 게이지 누적량. 게이지 고갈 시 Broken.")]
    [Min(0f)] public float breakGaugeDamage;
    public HitType hitType;
    public AttackKnockbackData knockback;
    public AttackLaunchData targetLaunch;
    public AttackDownData landingDown;
    public AttackLifeStealData lifeSteal;
    public string hitVfxAddress;
    public SfxType hitSfx;
    public AttackTimeScaleData hitstop;
    public AttackCameraShakeData cameraShake;
    public AttackCameraCueData cameraCue;
}

[Serializable]
public struct AttackDeliveryFlow
{
    public bool endAttackIfNoHitByEventEnd;
    public bool stopMovementOnHit;
}

[Serializable]
public struct AttackDeliveryEvent
{
    public string label;
    public bool enabled;
    [Min(0f)] public float startTime;
    public AttackDeliveryType type;
    public AttackMeleeDelivery melee;
    public AttackProjectileDelivery projectile;
    public AttackFieldDelivery field;
    public AttackHitDeduplication dedupe;
    public AttackHitResultData hitResult;
}

[Serializable]
public struct AttackMovementEvent
{
    public string label;
    public bool enabled;
    [Min(0f)] public float startTime;
    [Min(0f)] public float duration;
    public AttackMovementType type;
    [Tooltip("Lunge 전진 거리. 음수=후진(백스텝). Slam/SelfJump/Suspend에선 미사용.")]
    public float distance;
    [Tooltip("Slam 하강 속도(m/s). Lunge/SelfJump/Suspend에선 미사용.")]
    [Min(0f)] public float speed;
    [Min(0f)] public float height;
    public AnimationCurve curve;
}

[Serializable]
public struct AttackFeedbackEvent
{
    public string label;
    public bool enabled;
    public AttackFeedbackTrigger trigger;
    [Min(0f)] public float startTime;
    [Tooltip("스폰한 (Actor 공간) VFX를 이 시각(공격 진행 elapsed)에 디스폰. 0이면 공격 종료까지/VFX 자체 수명 유지.")]
    [Min(0f)] public float endTime;
    [Min(-1)] public int deliveryIndex;
    public bool localPlayerOnly;
    public bool deferUntilWindupEnd;
    [Tooltip("이 이벤트 창(startTime~endTime) 동안 모션 잔상을 연속 방출. endTime<=0이면 공격 종료에서 정지. Timeline 트리거 전용.")]
    public bool motionAfterimages;

    public string vfxAddress;
    public AttackFeedbackVfxOrigin vfxOrigin;
    public Vector3 vfxOffset;
    public Vector3 vfxEuler;
    public Vector3 vfxScale;

    public SfxType sfx;
    public AttackCameraShakeData cameraShake;
    public AttackTimeScaleData slowMo;
    public AttackCameraCueData cameraCue;
}

// ─────────────────────────────────────────────────────────────────────────────
// SO_Attack_Data
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "SO_Attack_Data", menuName = "Game/Combat/Attack Data")]
public sealed class SO_Attack_Data : ScriptableObject
{
    public const int CurrentSchemaVersion = 2;

    [SerializeField, HideInInspector] private int schemaVersion;

    // ── Setup ────────────────────────────────────────────────────────────────
    // 공격 식별·스칼라: 애니메이션, 전체 길이, 시전자 poise(슈퍼아머).
    [Header("Setup")]
    [SerializeField] private AttackAnimationData animation = new()
    {
        stateName = "Attack0",
        transition = 0.05f,
        timingMode = AttackAnimationTimingMode.PlayAtNormalSpeed,
        speed = 1f
    };
    [SerializeField, Min(0f)] private float totalDuration = 0.4f;
    // 시전자 자신의 poise(슈퍼아머). delivery 단위가 아니라 공격 전체에 걸리므로 여기 둔다.
    [SerializeField] private AttackSuperArmorData superArmor;

    // ── Timeline ─────────────────────────────────────────────────────────────
    [Header("Timeline")]
    [SerializeField] private AttackDeliveryEvent[] deliveryEvents;
    [SerializeField] private AttackMovementEvent[] movementEvents;
    [SerializeField] private AttackFeedbackEvent[] feedbackEvents;

    // ── Properties ───────────────────────────────────────────────────────────
    public int SchemaVersion => schemaVersion;
    public AttackAnimationData Animation => animation;
    public float TotalDuration => totalDuration;
    public AttackSuperArmorData SuperArmorData => superArmor;
    public float SuperArmor => superArmor.value;
    public AttackDeliveryEvent[] DeliveryEvents => deliveryEvents;
    public AttackMovementEvent[] MovementEvents => movementEvents;
    public AttackFeedbackEvent[] FeedbackEvents => feedbackEvents;
    public bool HasDeliveryEvents => deliveryEvents != null && deliveryEvents.Length > 0;
    public bool HasMovementEvents => movementEvents != null && movementEvents.Length > 0;
    public bool HasFeedbackEvents => feedbackEvents != null && feedbackEvents.Length > 0;
}

// ─────────────────────────────────────────────────────────────────────────────
// AttackShapeUtility — 히트박스 도형 판정 헬퍼 (NavAttackResolveSystem과 로직 미러)
// ─────────────────────────────────────────────────────────────────────────────

public static class AttackTimelineUtility
{
    public static float GetDuration(SO_Attack_Data attack)
        => attack != null ? Mathf.Max(0.01f, attack.TotalDuration) : 0f;

    // 컷인 취소 가드용: 이 공격이 지정 트리거의 카메라 큐를 들고 있는지(연출 자체는 feedback/hit 경로가 재생).
    public static bool HasCameraCue(SO_Attack_Data attack, AttackCueTrigger trigger)
    {
        if (attack == null) return false;
        AttackFeedbackEvent[] events = attack.FeedbackEvents;
        if (events != null)
            for (int i = 0; i < events.Length; i++)
                if (events[i].enabled && events[i].cameraCue.enabled && events[i].cameraCue.trigger == trigger)
                    return true;
        if (trigger == AttackCueTrigger.Hit)
        {
            AttackDeliveryEvent[] deliveries = attack.DeliveryEvents;
            if (deliveries != null)
                for (int i = 0; i < deliveries.Length; i++)
                    if (deliveries[i].enabled && deliveries[i].hitResult.cameraCue.enabled
                        && deliveries[i].hitResult.cameraCue.trigger == AttackCueTrigger.Hit)
                        return true;
        }
        return false;
    }

    public static bool HasAnyCameraCue(SO_Attack_Data attack)
    {
        if (attack == null) return false;
        AttackFeedbackEvent[] events = attack.FeedbackEvents;
        if (events != null)
            for (int i = 0; i < events.Length; i++)
                if (events[i].enabled && events[i].cameraCue.enabled)
                    return true;
        AttackDeliveryEvent[] deliveries = attack.DeliveryEvents;
        if (deliveries != null)
            for (int i = 0; i < deliveries.Length; i++)
                if (deliveries[i].enabled && deliveries[i].hitResult.cameraCue.enabled)
                    return true;
        return false;
    }

    public static bool TryGetFirstEnabledDelivery(
        SO_Attack_Data attack,
        out AttackDeliveryEvent delivery,
        out AttackHitboxData hitbox,
        out AttackShapeData shape)
    {
        if (attack != null && attack.HasDeliveryEvents)
        {
            AttackDeliveryEvent[] deliveries = attack.DeliveryEvents;
            int selectedIndex = -1;
            float selectedStartTime = float.MaxValue;
            for (int i = 0; i < deliveries.Length; i++)
            {
                if (!deliveries[i].enabled || deliveries[i].startTime >= selectedStartTime) continue;
                selectedIndex = i;
                selectedStartTime = deliveries[i].startTime;
            }
            if (selectedIndex >= 0)
            {
                delivery = deliveries[selectedIndex];
                ResolveDeliveryHitVolume(delivery, out hitbox, out shape);
                return true;
            }
        }

        delivery = default;
        hitbox = default;
        shape = default;
        return false;
    }

    public static float GetFirstDeliveryStartTime(SO_Attack_Data attack)
    {
        if (TryGetFirstEnabledDelivery(attack, out AttackDeliveryEvent delivery, out _, out _))
            return Mathf.Max(0f, delivery.startTime);
        return GetDuration(attack);
    }

    public static bool TryGetPrimaryHitVolume(SO_Attack_Data attack, out AttackHitboxData hitbox, out AttackShapeData shape)
    {
        if (attack != null && attack.HasDeliveryEvents)
        {
            AttackDeliveryEvent[] deliveries = attack.DeliveryEvents;
            for (int pass = 0; pass < 3; pass++)
            {
                AttackDeliveryType preferred = pass switch
                {
                    0 => AttackDeliveryType.Melee,
                    1 => AttackDeliveryType.Field,
                    _ => AttackDeliveryType.Projectile
                };
                for (int i = 0; i < deliveries.Length; i++)
                {
                    AttackDeliveryEvent delivery = deliveries[i];
                    if (!delivery.enabled || delivery.type != preferred) continue;
                    ResolveDeliveryHitVolume(delivery, out hitbox, out shape);
                    return true;
                }
            }
        }

        hitbox = default;
        shape = default;
        return false;
    }

    public static float GetTargetingRange(SO_Attack_Data attack)
    {
        if (attack == null) return 0f;
        float range = 0f;
        AttackDeliveryEvent[] deliveries = attack.DeliveryEvents;
        for (int i = 0; i < deliveries.Length; i++)
        {
            AttackDeliveryEvent delivery = deliveries[i];
            if (!delivery.enabled) continue;
            float deliveryRange = delivery.type switch
            {
                AttackDeliveryType.Projectile => ResolveProjectileRange(delivery.projectile),
                AttackDeliveryType.Field => ResolveFieldRange(delivery.field),
                _ => delivery.melee.hitbox.offset + AttackShapeUtility.GetPlanarReach(delivery.melee.shape)
            };
            range = Mathf.Max(range, deliveryRange);
        }
        return range;
    }

    public static float GetMaxForwardMovementDistance(SO_Attack_Data attack)
    {
        if (attack == null) return 0f;
        float distance = 0f;
        AttackMovementEvent[] movements = attack.MovementEvents;
        if (movements == null) return 0f;
        for (int i = 0; i < movements.Length; i++)
            if (movements[i].enabled && IsForwardMovement(movements[i].type))
                distance = Mathf.Max(distance, Mathf.Max(0f, movements[i].distance));
        return distance;
    }

    // 다단(repeat) 멜리 delivery가 있는지 — 텔레그래프 레인(휩쓸기 경로) 판정용.
    public static bool HasRepeatingMeleeDelivery(SO_Attack_Data attack)
    {
        if (attack == null) return false;
        AttackDeliveryEvent[] deliveries = attack.DeliveryEvents;
        if (deliveries == null) return false;
        for (int i = 0; i < deliveries.Length; i++)
            if (deliveries[i].enabled
                && deliveries[i].type == AttackDeliveryType.Melee
                && deliveries[i].melee.repeat.enabled)
                return true;
        return false;
    }

    public static bool HasSelfJump(SO_Attack_Data attack)
    {
        if (attack == null) return false;
        AttackMovementEvent[] movements = attack.MovementEvents;
        if (movements == null) return false;
        for (int i = 0; i < movements.Length; i++)
            if (movements[i].enabled && movements[i].type == AttackMovementType.SelfJump)
                return true;
        return false;
    }

    public static bool TryGetFirstProjectile(SO_Attack_Data attack, out AttackProjectileDelivery projectile)
    {
        if (attack != null && attack.HasDeliveryEvents)
        {
            AttackDeliveryEvent[] deliveries = attack.DeliveryEvents;
            for (int i = 0; i < deliveries.Length; i++)
                if (deliveries[i].enabled && deliveries[i].type == AttackDeliveryType.Projectile)
                {
                    projectile = deliveries[i].projectile;
                    return true;
                }
        }
        projectile = default;
        return false;
    }

    public static float GetProjectileRange(AttackProjectileDelivery projectile)
        => ResolveProjectileRange(projectile);

    private static float ResolveProjectileRange(AttackProjectileDelivery projectile)
    {
        float maxDistance = Mathf.Max(0f, projectile.maxDistance);
        float lifetimeDistance = projectile.speed > 0f && projectile.lifetime > 0f
            ? projectile.speed * projectile.lifetime
            : 0f;
        if (maxDistance > 0f && lifetimeDistance > 0f)
            return Mathf.Min(maxDistance, lifetimeDistance);
        return Mathf.Max(maxDistance, lifetimeDistance);
    }

    private static float ResolveFieldRange(AttackFieldDelivery field)
        => field.origin == FieldOrigin.ProjectileImpact
            ? 0f
            : Mathf.Max(0f, field.forwardOffset) + AttackShapeUtility.GetPlanarReach(field.shape);

    private static void ResolveDeliveryHitVolume(
        AttackDeliveryEvent delivery,
        out AttackHitboxData hitbox,
        out AttackShapeData shape)
    {
        if (delivery.type == AttackDeliveryType.Projectile)
        {
            hitbox = delivery.projectile.hitbox;
            shape = delivery.projectile.shape;
            return;
        }
        if (delivery.type == AttackDeliveryType.Field)
        {
            hitbox = delivery.field.hitbox;
            if (delivery.field.origin == FieldOrigin.ForwardOffset)
                hitbox.offset += delivery.field.forwardOffset;
            shape = delivery.field.shape;
            return;
        }
        hitbox = delivery.melee.hitbox;
        shape = delivery.melee.shape;
    }

    private static bool IsForwardMovement(AttackMovementType type)
        => type == AttackMovementType.Lunge;
}

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
        float verticalTolerance = Mathf.Max(0f, shape.verticalTolerance);
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
        if (!IsWithinVerticalTolerance(origin, targetPosition, targetRadius, shape))
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
        if (!IsWithinVerticalTolerance(center, targetPosition, targetRadius, shape))
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
        if (!IsWithinVerticalTolerance(center, targetPosition, targetRadius, shape))
            return false;

        Vector3 delta = Flatten(targetPosition - center);
        float radius = Mathf.Max(0f, shape.radius) + targetRadius;
        return delta.sqrMagnitude <= radius * radius;
    }

    private static bool IsWithinVerticalTolerance(Vector3 origin, Vector3 targetPosition, float targetRadius, AttackShapeData shape)
    {
        float verticalTolerance = Mathf.Max(0f, shape.verticalTolerance) + targetRadius;
        return Mathf.Abs(targetPosition.y - origin.y) <= verticalTolerance;
    }
}
