using System.Collections.Generic;
using MapNav.Baking;
using MapNav.Data;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MapNavigationAuthoring : MonoBehaviour
{
    [SerializeField] private List<MapNavRegion> regions = new();
    [SerializeField] private List<MapNavTransition> transitions = new();

    private BlobAssetReference<NavBlob> _navBlobData;
    private bool _blobDataDirty = true;

    public IReadOnlyList<MapNavRegion> Regions => regions;
    public IReadOnlyList<MapNavTransition> Transitions => transitions;

    public BlobAssetReference<NavBlob> NavBlobData
    {
        get
        {
            EnsureBlobDataCurrent();
            return _navBlobData;
        }
    }

    private void Awake()
    {
        RebuildRuntimeData();
    }

    private void OnValidate()
    {
        RebuildRuntimeData(false);
    }

    public void RebuildRuntimeData()
    {
        RebuildRuntimeData(true);
    }

    private void RebuildRuntimeData(bool rebuildBlobData)
    {
        RecalculateBounds();
        _blobDataDirty = true;

        if (rebuildBlobData)
            RebuildBlobData();
    }

    private void RecalculateBounds()
    {
        for (int i = 0; i < regions.Count; i++)
            regions[i]?.RecalculateBounds();

        for (int i = 0; i < transitions.Count; i++)
            transitions[i]?.RecalculateBounds();
    }

    private void RebuildBlobData()
    {
        DisposeBlobData();
        _navBlobData = MapNavBaker.Build(this, Allocator.Persistent);
        _blobDataDirty = false;
    }

    private void EnsureBlobDataCurrent()
    {
        if (!_blobDataDirty && _navBlobData.IsCreated)
            return;

        RebuildBlobData();
    }

    private void OnDestroy()
    {
        DisposeBlobData();
    }

    private void DisposeBlobData()
    {
        if (_navBlobData.IsCreated)
            _navBlobData.Dispose();

        _blobDataDirty = true;
    }

    public Vector3 ToWorld(MapNavRegion region, Vector2 localPoint)
    {
        float height = region != null ? region.GetHeight(localPoint) : 0f;
        return transform.TransformPoint(new Vector3(localPoint.x, height, localPoint.y));
    }

    public Vector3 ToWorld(MapNavTransition transition, Vector2 localPoint)
    {
        float height = transition != null ? transition.GetHeight(localPoint) : 0f;
        return transform.TransformPoint(new Vector3(localPoint.x, height, localPoint.y));
    }

    public void AddRegion(MapNavRegion region)
    {
        regions.Add(region);
        RebuildRuntimeData();
    }

    public void AddTransition(MapNavTransition transition)
    {
        transitions.Add(transition);
        RebuildRuntimeData();
    }

    public int GetNextRegionId()
    {
        int maxId = -1;
        for (int i = 0; i < regions.Count; i++)
            maxId = Mathf.Max(maxId, regions[i].Id);

        return maxId + 1;
    }

    public int GetNextTransitionId()
    {
        int maxId = -1;
        for (int i = 0; i < transitions.Count; i++)
            maxId = Mathf.Max(maxId, transitions[i].Id);

        return maxId + 1;
    }

    private void Reset()
    {
        if (regions.Count > 0)
            return;

        regions.Add(new MapNavRegion
        {
            Id = 0,
            NavLayerId = 0,
            Height = 0f,
            Points =
            {
                new Vector2(-2f, -2f),
                new Vector2(-2f, 2f),
                new Vector2(2f, 2f),
                new Vector2(2f, -2f)
            }
        });
    }
}
