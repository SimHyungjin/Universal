using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

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

        void Execute(ref NavAgentLaunch launch)
        {
            if (launch.Height <= 0f || launch.Duration <= 0f)
            {
                launch.VisualYOffset = 0f;
                return;
            }

            float totalDuration = launch.SuspendAtApex != 0
                ? math.max(launch.Duration, launch.SuspendDuration)
                : launch.Duration;

            if (launch.Elapsed >= totalDuration)
            {
                launch.VisualYOffset = 0f;
                return;
            }

            if (launch.FreezeTimer > 0f)
                launch.FreezeTimer = math.max(0f, launch.FreezeTimer - DeltaTime);
            else
                launch.Elapsed += DeltaTime;

            float t = GetLaunchCurveT(launch.Elapsed, launch.Duration, totalDuration, launch.SuspendAtApex != 0);
            launch.VisualYOffset = 4f * launch.Height * t * (1f - t);
        }

        static float GetLaunchCurveT(float elapsed, float arcDuration, float totalDuration, bool suspendAtApex)
        {
            if (!suspendAtApex)
                return math.clamp(elapsed / arcDuration, 0f, 1f);

            float halfArcDuration = arcDuration * 0.5f;
            if (elapsed < halfArcDuration)
                return math.clamp(elapsed / arcDuration, 0f, 1f);

            float fallStart = math.max(halfArcDuration, totalDuration - halfArcDuration);
            if (elapsed < fallStart)
                return 0.5f;

            return math.clamp(0.5f + (elapsed - fallStart) / arcDuration, 0f, 1f);
        }
    }
}
