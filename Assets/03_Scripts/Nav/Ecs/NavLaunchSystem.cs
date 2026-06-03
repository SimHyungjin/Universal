using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MapNav.Ecs
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NavKnockbackSystem))]
    public partial struct NavLaunchSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new NavLaunchJob { DeltaTime = SystemAPI.Time.DeltaTime }.ScheduleParallel();
        }
    }

    [BurstCompile]
    partial struct NavLaunchJob : IJobEntity
    {
        public float DeltaTime;

        void Execute(ref NavAgentLaunch launch, ref LocalTransform transform)
        {
            if (launch.Airborne == 0)
                return;

            float y = transform.Position.y;
            float ceiling = launch.GroundY + launch.Height;
            LaunchPhysics.Integrate(ref y, ref launch.VerticalVelocity, LaunchPhysics.Gravity, DeltaTime, ref launch.SuspendTimer, ceiling);

            // 낙하해서 지면(시작 높이) 이하로 내려오면 착지. y를 지면에 맞추고 Airborne 해제 →
            // 다음 프레임부터 NavMovementSystem의 height snap이 다시 작동한다.
            if (launch.VerticalVelocity <= 0f && y <= launch.GroundY)
            {
                y = launch.GroundY;
                launch.VerticalVelocity = 0f;
                launch.Airborne = 0;
            }

            float3 pos = transform.Position;
            pos.y = y;
            transform.Position = pos;
        }
    }
}
