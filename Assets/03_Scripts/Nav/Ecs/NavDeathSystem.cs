using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace MapNav.Ecs
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NavMovementSystem))]
    public partial struct NavDeathSystem : ISystem
    {
        // HP가 0이 된 뒤 넉백으로 날아가고 사망(쓰러짐) 애니메이션이 재생될 시간.
        // 이 시간이 지나면 (파괴 대신) 반대 진영으로 부활시킨다 — 죽음→전향 무쌍.
        // 적을 죽이면 아군이 되고, 아군이 죽으면 적이 되는 제로섬 토글. 실체화된 잡몹은 사실상 불멸이다(설계상 의도).
        private const float DeathDuration = 1.2f;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NavAgentDeath>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;

            ComponentLookup<NavAgentMotion> motionLookup = SystemAPI.GetComponentLookup<NavAgentMotion>();
            ComponentLookup<NavAgentTarget> targetLookup = SystemAPI.GetComponentLookup<NavAgentTarget>();
            ComponentLookup<NavAgentPathRequest> requestLookup = SystemAPI.GetComponentLookup<NavAgentPathRequest>();
            ComponentLookup<NavAgentPathStatus> statusLookup = SystemAPI.GetComponentLookup<NavAgentPathStatus>();
            BufferLookup<NavAgentWaypoint> waypointLookup = SystemAPI.GetBufferLookup<NavAgentWaypoint>();

            foreach (var (death, health, knockback, faction, settings, entity) in
                SystemAPI.Query<
                    RefRW<NavAgentDeath>,
                    RefRW<NavAgentHealth>,
                    RefRW<NavAgentKnockback>,
                    RefRW<NavAgentFaction>,
                    RefRO<NavAgentSettings>>()
                    .WithEntityAccess())
            {
                if (death.ValueRO.Dying == 0)
                {
                    if (health.ValueRO.Current > 0f)
                        continue;

                    death.ValueRW.Dying = 1;
                    death.ValueRW.Timer = DeathDuration;
                    StopNavigation(entity, ref motionLookup, ref targetLookup, ref requestLookup, ref statusLookup, ref waypointLookup);
                    knockback.ValueRW.MotionLockTimer =
                        math.max(knockback.ValueRO.MotionLockTimer, DeathDuration);
                    continue;
                }

                StopNavigation(entity, ref motionLookup, ref targetLookup, ref requestLookup, ref statusLookup, ref waypointLookup);
                death.ValueRW.Timer -= dt;

                // 넉백이 끝나도 사망 연출이 끝날 때까지 길찾기/이동이 재개되지 않도록 잠금을 유지.
                knockback.ValueRW.MotionLockTimer =
                    math.max(knockback.ValueRO.MotionLockTimer, death.ValueRO.Timer);

                if (death.ValueRO.Timer <= 0f)
                    Revive(ref death.ValueRW, ref health.ValueRW, ref knockback.ValueRW, ref faction.ValueRW, settings.ValueRO);
            }
        }

        // 쓰러짐 연출이 끝난 잡몹을 반대 진영으로 되살린다(적↔아군 토글). ECS는 진실(faction/HP/dying)만
        // 갱신하고, 비주얼 셸(Unit_NavVisualShell)이 사망→부활 전환을 감지해 파티클·머테리얼·wakeup 연출을 재생한다.
        private static void Revive(
            ref NavAgentDeath death,
            ref NavAgentHealth health,
            ref NavAgentKnockback knockback,
            ref NavAgentFaction faction,
            in NavAgentSettings settings)
        {
            death.Dying = 0;
            death.Timer = 0f;
            health.Current = health.Max;
            faction.Faction = faction.Faction == NavFaction.Ally ? NavFaction.Enemy : NavFaction.Ally;

            // 사망 잠금은 풀되, wakeup 애니메이션이 재생되는 동안만 이동/길찾기를 잠근다.
            // 잠금이 풀리는 순간 NavKnockbackSystem이 target.Dirty=1로 재타겟을 트리거한다.
            float wakeup = math.max(0f, settings.WakeupRecoveryDuration);
            knockback.MotionLockTimer = wakeup;
            knockback.WakeupTimer = wakeup;
            knockback.Velocity = float3.zero;
            knockback.IsHeavy = 0;
            knockback.HitVersion = 0;
        }

        private static void StopNavigation(
            Entity entity,
            ref ComponentLookup<NavAgentMotion> motionLookup,
            ref ComponentLookup<NavAgentTarget> targetLookup,
            ref ComponentLookup<NavAgentPathRequest> requestLookup,
            ref ComponentLookup<NavAgentPathStatus> statusLookup,
            ref BufferLookup<NavAgentWaypoint> waypointLookup)
        {
            if (targetLookup.HasComponent(entity))
            {
                NavAgentTarget target = targetLookup[entity];
                target.Dirty = 0;
                targetLookup[entity] = target;
            }

            if (requestLookup.HasComponent(entity))
            {
                NavAgentPathRequest request = requestLookup[entity];
                request.Pending = 0;
                requestLookup[entity] = request;
            }

            if (statusLookup.HasComponent(entity))
            {
                NavAgentPathStatus status = statusLookup[entity];
                status.HasPath = 0;
                status.Waiting = 0;
                status.Failed = 0;
                statusLookup[entity] = status;
            }

            if (motionLookup.HasComponent(entity))
            {
                NavAgentMotion motion = motionLookup[entity];
                motion.IsMoving = 0;
                motion.CurrentSpeed = 0f;
                motion.StuckTimer = 0f;
                motion.RepathCooldownRemaining = 0f;
                motion.LastDistanceToWaypoint = 0f;
                motion.Velocity = float3.zero;
                motionLookup[entity] = motion;
            }

            if (waypointLookup.HasBuffer(entity))
                waypointLookup[entity].Clear();
        }
    }
}
