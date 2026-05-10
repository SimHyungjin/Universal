using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace MapNav.Ecs
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct NavTargetCommandSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach ((RefRO<NavAgentTargetCommand> command, RefRW<NavAgentTarget> target, RefRW<NavAgentMotion> motion, Entity entity)
                in SystemAPI.Query<RefRO<NavAgentTargetCommand>, RefRW<NavAgentTarget>, RefRW<NavAgentMotion>>().WithEntityAccess())
            {
                target.ValueRW.Position = command.ValueRO.Position;
                target.ValueRW.Dirty = 1;
                motion.ValueRW.StuckRetryCount = 0;
                ecb.RemoveComponent<NavAgentTargetCommand>(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
