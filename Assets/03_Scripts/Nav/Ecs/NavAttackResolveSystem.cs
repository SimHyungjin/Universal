using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MapNav.Ecs
{
    // NavAttackSystem이 HitPending=1로 세운 적군 공격을 소비해 플레이어 피격 이벤트로 변환한다.
    // 판정 프레임에 플레이어 현재 위치와 AttackRange로 사거리를 재검증해 회피 여지를 남긴다.
    // 적군(NavFaction.Enemy)만 다룬다. 아군 잡몹의 HitPending은 별도 시스템이 처리한다.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NavAttackSystem))]
    public partial struct NavAttackResolveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NavAgentAttack>();
        }

        public void OnUpdate(ref SystemState state)
        {
            EntityManager em = state.EntityManager;
            bool hasPlayer = SystemAPI.TryGetSingleton(out PlayerNavTarget player) && player.HasValue != 0;
            Entity playerEntity = hasPlayer ? SystemAPI.GetSingletonEntity<PlayerNavTarget>() : Entity.Null;
            bool hasPlayerInbox = hasPlayer && em.HasBuffer<PlayerIncomingHit>(playerEntity);
            DynamicBuffer<PlayerIncomingHit> inbox = default;
            if (hasPlayerInbox)
                inbox = em.GetBuffer<PlayerIncomingHit>(playerEntity);

            foreach (var (attack, settings, profile, faction, death, combat, transform) in
                SystemAPI.Query<
                    RefRW<NavAgentAttack>,
                    RefRO<NavAgentSettings>,
                    RefRO<NavAgentAttackProfile>,
                    RefRO<NavAgentFaction>,
                    RefRO<NavAgentDeath>,
                    RefRO<NavAgentCombatTarget>,
                    RefRO<LocalTransform>>())
            {
                if (attack.ValueRO.HitPending == 0)
                    continue;
                if (death.ValueRO.Dying != 0)
                {
                    attack.ValueRW.HitPending = 0;
                    continue;
                }

                float range = settings.ValueRO.AttackRange;
                if (range <= 0f || combat.ValueRO.HasTarget == 0)
                {
                    attack.ValueRW.HitPending = 0;
                    continue;
                }

                float3 forward = math.normalizesafe(
                    math.mul(transform.ValueRO.Rotation, new float3(0f, 0f, 1f)),
                    new float3(0f, 0f, 1f));

                if (combat.ValueRO.IsPlayer != 0)
                {
                    if (faction.ValueRO.Faction == NavFaction.Enemy && hasPlayerInbox && IsInAttackShape(transform.ValueRO.Position, forward, player.Position, player.HitRadius, profile.ValueRO))
                    {
                        inbox.Add(new PlayerIncomingHit
                        {
                            SourcePosition = transform.ValueRO.Position,
                            Attack = profile.ValueRO
                        });
                    }
                }
                else if (TryGetValidUnitTarget(em, combat.ValueRO.TargetEntity, faction.ValueRO.Faction, out LocalTransform targetTransform, out float targetRadius))
                {
                    if (IsInAttackShape(transform.ValueRO.Position, forward, targetTransform.Position, targetRadius, profile.ValueRO))
                        ApplyUnitHit(em, combat.ValueRO.TargetEntity, transform.ValueRO, profile.ValueRO);
                }

                attack.ValueRW.HitPending = 0;
            }
        }

        private static bool TryGetValidUnitTarget(EntityManager em, Entity target, NavFaction attackerFaction, out LocalTransform targetTransform, out float targetRadius)
        {
            targetTransform = default;
            targetRadius = 0f;
            if (target == Entity.Null || !em.Exists(target))
                return false;
            if (!em.HasComponent<LocalTransform>(target) || !em.HasComponent<NavAgentFaction>(target))
                return false;
            if (em.GetComponentData<NavAgentFaction>(target).Faction == attackerFaction)
                return false;
            if (em.HasComponent<NavAgentDeath>(target) && em.GetComponentData<NavAgentDeath>(target).Dying != 0)
                return false;

            targetTransform = em.GetComponentData<LocalTransform>(target);
            if (em.HasComponent<NavAgentSettings>(target))
                targetRadius = math.max(0f, em.GetComponentData<NavAgentSettings>(target).AgentRadius);
            return true;
        }

        private static bool IsInAttackShape(float3 source, float3 forward, float3 target, float targetRadius, in NavAgentAttackProfile profile)
        {
            forward.y = 0f;
            forward = math.normalizesafe(forward, new float3(0f, 0f, 1f));
            targetRadius = math.max(0f, targetRadius);

            return profile.Shape switch
            {
                AttackShape.Cone => IsInCone(source, forward, target, targetRadius, profile),
                AttackShape.Box => IsInBox(source, forward, target, targetRadius, profile),
                _ => IsInSphere(source, forward, target, targetRadius, profile)
            };
        }

        private static bool IsInSphere(float3 source, float3 forward, float3 target, float targetRadius, in NavAgentAttackProfile profile)
        {
            float3 center = source + forward * profile.HitboxOffset + new float3(0f, profile.HitboxYOffset, 0f);
            if (!IsWithinVerticalTolerance(center, target, profile.HitboxVerticalTolerance + targetRadius))
                return false;

            float3 delta = target - center;
            delta.y = 0f;
            float radius = math.max(0f, profile.ShapeRadius) + targetRadius;
            return math.lengthsq(delta) <= radius * radius;
        }

        private static bool IsInCone(float3 source, float3 forward, float3 target, float targetRadius, in NavAgentAttackProfile profile)
        {
            float3 origin = source + forward * profile.HitboxOffset + new float3(0f, profile.HitboxYOffset, 0f);
            if (!IsWithinVerticalTolerance(origin, target, profile.HitboxVerticalTolerance + targetRadius))
                return false;

            float3 delta = target - origin;
            delta.y = 0f;

            float length = math.max(profile.ShapeRadius, profile.ShapeLength) + targetRadius;
            float distSq = math.lengthsq(delta);
            if (distSq > length * length)
                return false;
            if (distSq <= targetRadius * targetRadius)
                return true;

            float angle = math.clamp(profile.ShapeAngle, 1f, 360f);
            if (angle >= 359.9f)
                return true;

            float distance = math.sqrt(distSq);
            float expandedHalfAngle = angle * 0.5f + math.degrees(math.atan2(targetRadius, math.max(0.0001f, distance)));
            float dot = math.dot(delta / distance, forward);
            return dot >= math.cos(math.radians(math.min(180f, expandedHalfAngle)));
        }

        private static bool IsInBox(float3 source, float3 forward, float3 target, float targetRadius, in NavAgentAttackProfile profile)
        {
            float length = math.max(profile.ShapeRadius * 2f, profile.ShapeLength);
            float width = math.max(profile.ShapeRadius * 2f, profile.ShapeWidth);
            float3 center = source + forward * (profile.HitboxOffset + length * 0.5f) + new float3(0f, profile.HitboxYOffset, 0f);
            if (!IsWithinVerticalTolerance(center, target, profile.HitboxVerticalTolerance + targetRadius))
                return false;

            float3 right = new float3(forward.z, 0f, -forward.x);
            float3 delta = target - center;
            delta.y = 0f;

            return math.abs(math.dot(delta, forward)) <= length * 0.5f + targetRadius
                   && math.abs(math.dot(delta, right)) <= width * 0.5f + targetRadius;
        }

        private static bool IsWithinVerticalTolerance(float3 origin, float3 target, float verticalTolerance)
            => math.abs(target.y - origin.y) <= math.max(0f, verticalTolerance);

        private static void ApplyUnitHit(EntityManager em, Entity target, in LocalTransform attackerTransform, in NavAgentAttackProfile profile)
        {
            if (em.HasComponent<NavAgentHealth>(target))
            {
                NavAgentHealth health = em.GetComponentData<NavAgentHealth>(target);
                float defense = em.HasComponent<NavAgentSettings>(target)
                    ? em.GetComponentData<NavAgentSettings>(target).Defense
                    : 0f;
                float taken = CombatFormula.ReduceIncomingDamage(defense, profile.Damage);
                health.Current -= taken;
                em.SetComponentData(target, health);
            }

            if (!em.HasComponent<NavAgentKnockback>(target) || IsSuperArmorBlocked(em, target, profile.SuperArmorBreak))
                return;

            LocalTransform targetTransform = em.GetComponentData<LocalTransform>(target);
            float3 radialDir = math.normalizesafe(
                new float3(targetTransform.Position.x - attackerTransform.Position.x, 0f, targetTransform.Position.z - attackerTransform.Position.z),
                new float3(0f, 0f, 1f));
            float3 attackerForward = math.mul(attackerTransform.Rotation, new float3(0f, 0f, 1f));
            float3 forwardDir = math.normalizesafe(
                new float3(attackerForward.x, 0f, attackerForward.z),
                new float3(0f, 0f, 1f));
            float3 dir = profile.KnockbackType == KnockbackType.Directional ? forwardDir : radialDir;
            float3 lookDir = math.normalizesafe(
                new float3(attackerTransform.Position.x - targetTransform.Position.x, 0f, attackerTransform.Position.z - targetTransform.Position.z),
                forwardDir);

            targetTransform.Rotation = quaternion.LookRotationSafe(lookDir, math.up());
            em.SetComponentData(target, targetTransform);

            int prevVersion = em.GetComponentData<NavAgentKnockback>(target).HitVersion;
            em.SetComponentData(target, new NavAgentKnockback
            {
                Velocity        = dir * profile.KnockbackForce,
                Timer           = profile.KnockbackDuration,
                MotionLockTimer = profile.DownDuration,
                Friction        = profile.KnockbackFriction,
                InitialSpeed    = profile.KnockbackForce,
                HitType         = (int)profile.HitType,
                SuperArmorBreak = profile.SuperArmorBreak,
                HitVersion      = prevVersion + 1,
                IsHeavy         = (byte)(profile.IsDownAttack != 0 ? 1 : 0)
            });
        }

        private static bool IsSuperArmorBlocked(EntityManager em, Entity target, float superArmorBreak)
        {
            if (!em.HasComponent<NavAgentAttack>(target) || !em.HasComponent<NavAgentAttackProfile>(target))
                return false;

            NavAgentAttack targetAttack = em.GetComponentData<NavAgentAttack>(target);
            if (targetAttack.Phase == NavAttackPhase.Idle)
                return false;

            NavAgentAttackProfile targetProfile = em.GetComponentData<NavAgentAttackProfile>(target);
            return targetProfile.SuperArmor > superArmorBreak;
        }
    }
}
