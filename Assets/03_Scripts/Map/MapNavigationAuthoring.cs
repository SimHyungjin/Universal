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

    private readonly MapNavigationRuntimeData _runtimeData = new();
    private MapNavigationBuildData _buildData = MapNavigationBuildData.Empty();
    private BlobAssetReference<MapNavigationBlob> _blobData;
    private BlobAssetReference<NavBlob> _navBlobData;
    private bool _blobDataDirty = true;

    public IReadOnlyList<MapNavRegion> Regions => regions;
    public IReadOnlyList<MapNavTransition> Transitions => transitions;
    public MapNavigationRuntimeData RuntimeData => _runtimeData;
    public MapNavigationBuildData BuildData => _buildData;
    public BlobAssetReference<MapNavigationBlob> BlobData
    {
        get
        {
            EnsureBlobDataCurrent();
            return _blobData;
        }
    }
    public BlobAssetReference<NavBlob> NavBlobData
    {
        get
        {
            EnsureBlobDataCurrent();
            return _navBlobData;
        }
    }

    public MapNavigationBuildDataContext BuildDataContext => new(
        _buildData,
        transform.localToWorldMatrix,
        transform.worldToLocalMatrix);
    public MapNavigationBlobDataContext BlobDataContext
    {
        get
        {
            EnsureBlobDataCurrent();
            return new MapNavigationBlobDataContext(
                _blobData,
                transform.localToWorldMatrix,
                transform.worldToLocalMatrix);
        }
    }
    public MapNavigationQueryContext QueryContext => new(
        regions,
        transitions,
        _runtimeData,
        transform.localToWorldMatrix,
        transform.worldToLocalMatrix);

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
        _runtimeData.Rebuild(regions, transitions);
        _buildData = MapNavigationBuildData.Build(regions, transitions);
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
        _blobData = MapNavigationBlobBuilder.CreateBlobAsset(_buildData, Allocator.Persistent);
        _navBlobData = MapNavBaker.Build(this, Allocator.Persistent);
        _blobDataDirty = false;
    }

    private void EnsureBlobDataCurrent()
    {
        if (!_blobDataDirty && _blobData.IsCreated)
            return;

        RebuildBlobData();
    }

    private void OnDestroy()
    {
        DisposeBlobData();
    }

    private void DisposeBlobData()
    {
        if (_blobData.IsCreated)
            _blobData.Dispose();

        if (_navBlobData.IsCreated)
            _navBlobData.Dispose();

        _blobDataDirty = true;
    }

    public MapNavRegion FindRegion(int regionId)
    {
        return _runtimeData.FindRegion(regionId);
    }

    public bool TryFindRegion(Vector3 worldPosition, out MapNavRegion region)
    {
        return TryFindRegion(worldPosition, 0f, out region);
    }

    public bool TryFindRegion(Vector3 worldPosition, float tolerance, out MapNavRegion region)
    {
        region = MapNavigationQuery.FindContainingRegion(QueryContext, worldPosition, tolerance);
        return region != null;
    }

    public bool TryFindTransition(Vector3 worldPosition, out MapNavTransition transition)
    {
        return TryFindTransition(worldPosition, 0f, out transition);
    }

    public bool TryFindTransition(Vector3 worldPosition, float tolerance, out MapNavTransition transition)
    {
        return MapNavigationQuery.TryFindContainingTransition(QueryContext, worldPosition, tolerance, out transition);
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
