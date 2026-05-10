using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public sealed class MapNavigationAuthoringBaker : Baker<MapNavigationAuthoring>
{
    public override void Bake(MapNavigationAuthoring authoring)
    {
        MapNavigationBuildData buildData = MapNavigationBuildData.FromAuthoring(authoring);
        BlobAssetReference<MapNavigationBlob> blob = MapNavigationBlobBuilder.CreateBlobAsset(buildData, Allocator.Persistent);
        AddBlobAsset(ref blob, out Unity.Entities.Hash128 _);

        Entity entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, new MapNavigationBlobComponent
        {
            Blob = blob,
            LocalToWorldMatrix = ToFloat4x4(authoring.transform.localToWorldMatrix),
            WorldToLocalMatrix = ToFloat4x4(authoring.transform.worldToLocalMatrix)
        });
    }

    private static float4x4 ToFloat4x4(Matrix4x4 matrix)
    {
        return new float4x4(
            matrix.m00, matrix.m01, matrix.m02, matrix.m03,
            matrix.m10, matrix.m11, matrix.m12, matrix.m13,
            matrix.m20, matrix.m21, matrix.m22, matrix.m23,
            matrix.m30, matrix.m31, matrix.m32, matrix.m33);
    }
}
