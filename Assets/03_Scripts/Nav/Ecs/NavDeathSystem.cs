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
        // HP가 0이 된 뒤 넉백으로 날아가고 사망 애니메이션이 재생될 시간.
        // 이 시간이 지나면 엔티티를 파괴한다.
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

            EntityCommandBuffer ecb = SystemAPI
                .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            ComponentLookup<NavAgentMotion> motionLookup = SystemAPI.GetComponentLookup<NavAgentMotion>();
            ComponentLookup<NavAgentTarget> targetLookup = SystemAPI.GetComponentLookup<NavAgentTarget>();
            ComponentLookup<NavAgentPathRequest> requestLookup = SystemAPI.GetComponentLookup<NavAgentPathRequest>();
            ComponentLookup<NavAgentPathStatus> statusLookup = SystemAPI.GetComponentLookup<NavAgentPathStatus>();
            BufferLookup<NavAgentWaypoint> waypointLookup = SystemAPI.GetBufferLookup<NavAgentWaypoint>();

            foreach (var (death, health, knockback, entity) in
                SystemAPI.Query<RefRW<NavAgentDeath>, RefRO<NavAgentHealth>, RefRW<NavAgentKnockback>>()
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
                    ecb.DestroyEntity(entity);
            }
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
