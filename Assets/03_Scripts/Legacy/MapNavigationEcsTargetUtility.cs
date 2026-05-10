using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public static class MapNavigationEcsTargetUtility
{
    public static void SetTarget(EntityManager entityManager, Entity entity, Vector3 targetPosition, bool force = false)
    {
        SetTarget(entityManager, entity, (float3)targetPosition, force);
    }

    public static void SetTarget(EntityManager entityManager, Entity entity, float3 targetPosition, bool force = false)
    {
        if (!entityManager.Exists(entity) || !entityManager.HasComponent<MapNavEcsTarget>(entity))
            return;

        MapNavEcsTarget target = entityManager.GetComponentData<MapNavEcsTarget>(entity);
        if (!force && target.Dirty != 0 && math.all(target.Position == targetPosition))
            return;

        target.Position = targetPosition;
        target.Dirty = 1;
        entityManager.SetComponentData(entity, target);
    }

    public static void ClearTarget(EntityManager entityManager, Entity entity)
    {
        if (!entityManager.Exists(entity) || !entityManager.HasComponent<MapNavEcsTarget>(entity))
            return;

        MapNavEcsTarget target = entityManager.GetComponentData<MapNavEcsTarget>(entity);
        target.Dirty = 0;
        entityManager.SetComponentData(entity, target);
    }

    public static void SetTarget(EntityCommandBuffer commandBuffer, Entity entity, Vector3 targetPosition)
    {
        SetTarget(commandBuffer, entity, (float3)targetPosition);
    }

    public static void SetTarget(EntityCommandBuffer commandBuffer, Entity entity, float3 targetPosition)
    {
        commandBuffer.AddComponent(entity, new MapNavEcsTargetCommand
        {
            Position = targetPosition
        });
    }

    public static void ClearTarget(EntityCommandBuffer commandBuffer, Entity entity)
    {
        commandBuffer.SetComponent(entity, new MapNavEcsTarget { Dirty = 0 });
    }
}
