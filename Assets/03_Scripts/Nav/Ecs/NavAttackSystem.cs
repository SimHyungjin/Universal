using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MapNav.Ecs
{
    // 교전 타겟이 사거리 안에 들어오면 선딜(Windup) -> 판정 -> 쿨다운(Recover) 상태머신을 돈다.
    // 판정 프레임에 HitPending을 세워 두면 데미지 시스템이 이를 소비한다.
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NavKnockbackSystem))]
    [UpdateBefore(typeof(NavMovementSystem))]
    public partial struct NavAttackSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NavAgentAttack>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;

            foreach (var (attack, settings, combat, death, knockback, transform) in
                SystemAPI.Query<
                    RefRW<NavAgentAttack>,
                    RefRO<NavAgentSettings>,
                    RefRO<NavAgentCombatTarget>,
                    RefRO<NavAgentDeath>,
                    RefRO<NavAgentKnockback>,
                    RefRW<LocalTransform>>())
            {
                if (death.ValueRO.Dying != 0)
                {
                    attack.ValueRW.Phase = NavAttackPhase.Idle;
                    attack.ValueRW.Timer = 0f;
                    attack.ValueRW.HitPending = 0;
                    continue;
                }

                // 넉백·경직 중에는 진행 중인 선딜이 캔슬된다. 쿨다운(Recover)은 그대로 이어진다.
                if (NavKnockbackSystem.HasPlanarKnockbackVelocity(knockback.ValueRO.Velocity)
                    || knockback.ValueRO.MotionLockTimer > 0f
                    || knockback.ValueRO.WakeupTimer > 0f)
                {
                    if (attack.ValueRO.Phase == NavAttackPhase.Windup)
                    {
                        attack.ValueRW.Phase = NavAttackPhase.Idle;
                        attack.ValueRW.Timer = 0f;
                    }
                    continue;
                }

                switch (attack.ValueRO.Phase)
                {
                    case NavAttackPhase.Windup:
                    {
                        float t = attack.ValueRO.Timer - dt;
                        if (t <= 0f)
                        {
                            attack.ValueRW.HitPending = 1;
                            attack.ValueRW.Phase = NavAttackPhase.Recover;
                            attack.ValueRW.Timer = math.max(0f, settings.ValueRO.AttackCooldown);
                        }
                        else
                        {
                            attack.ValueRW.Timer = t;
                        }
                        break;
                    }
                    case NavAttackPhase.Recover:
                    {
                        float t = attack.ValueRO.Timer - dt;
                        if (t <= 0f)
                        {
                            if (!TryStartAttack(ref attack.ValueRW, settings.ValueRO, combat.ValueRO, ref transform.ValueRW))
                            {
                                attack.ValueRW.Phase = NavAttackPhase.Idle;
                                attack.ValueRW.Timer = 0f;
                            }
                        }
                        else
                        {
                            attack.ValueRW.Timer = t;
                        }
                        break;
                    }
                    default: // Idle
                    {
                        TryStartAttack(ref attack.ValueRW, settings.ValueRO, combat.ValueRO, ref transform.ValueRW);
                        break;
                    }
                }
            }
        }

        private static bool TryStartAttack(
            ref NavAgentAttack attack,
            in NavAgentSettings settings,
            in NavAgentCombatTarget combat,
            ref LocalTransform transform)
        {
            if (combat.HasTarget == 0)
                return false;

            float range = settings.AttackRange;
            if (range <= 0f)
                return false;

            float3 toTarget = combat.Position - transform.Position;
            toTarget.y = 0f;
            if (math.lengthsq(toTarget) > range * range)
                return false;

            attack.Phase = NavAttackPhase.Windup;
            attack.Timer = math.max(0f, settings.AttackWindup);
            attack.HitPending = 0;

            if (math.lengthsq(toTarget) > 1e-6f)
                transform.Rotation = quaternion.LookRotationSafe(math.normalize(toTarget), math.up());

            return true;
        }
    }
}
