using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MapNavigationEcsSettingsAuthoring : MonoBehaviour
{
    [SerializeField] private int maxPathsPerFrame = 16;

    public int MaxPathsPerFrame => maxPathsPerFrame;
}

public sealed class MapNavigationEcsSettingsAuthoringBaker : Baker<MapNavigationEcsSettingsAuthoring>
{
    public override void Bake(MapNavigationEcsSettingsAuthoring authoring)
    {
        Entity entity = GetEntity(TransformUsageFlags.None);
        AddComponent(entity, new MapNavEcsPathBuildBudget
        {
            MaxPathsPerFrame = math.max(1, authoring.MaxPathsPerFrame)
        });
    }
}
