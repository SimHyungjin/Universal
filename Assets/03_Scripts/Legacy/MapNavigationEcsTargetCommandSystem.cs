using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(MapNavigationEcsTargetResolveSystem))]
public partial struct MapNavigationEcsTargetCommandSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer ecb = new(Unity.Collections.Allocator.Temp);
        foreach ((RefRO<MapNavEcsTargetCommand> command, RefRW<MapNavEcsTarget> target, Entity entity)
            in SystemAPI.Query<RefRO<MapNavEcsTargetCommand>, RefRW<MapNavEcsTarget>>().WithEntityAccess())
        {
            target.ValueRW.Position = command.ValueRO.Position;
            target.ValueRW.Dirty = 1;
            ecb.RemoveComponent<MapNavEcsTargetCommand>(entity);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
