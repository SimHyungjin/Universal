using System.Collections.Generic;
using MapNav.Core;
using MapNav.Data;
using MapNav.Ecs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MapNavMonoAgent : MonoBehaviour
{
    [SerializeField] private MapNavigationAuthoring map;
    [SerializeField] private float agentRadius = 0.25f;
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float stopDistance = 0.08f;
    [SerializeField] private float waypointAdvanceDistance = 0.18f;
    [SerializeField] private float cornerLookAheadDistance = 0.35f;
    [SerializeField] private float heightOffset;
    [SerializeField] private float boundaryTolerance = 0.05f;

    [Header("Stuck recovery")]
    [SerializeField] private float stuckRepathDelay = 0.75f;
    [SerializeField] private float stuckRepathCooldown = 1.5f;
    [SerializeField] private float stuckProgressDistance = 0.03f;
    [SerializeField] private int stuckRetryLimit = 4;

    [SerializeField] private bool snapHeight = true;
    [SerializeField] private bool drawPath = true;
    [SerializeField] private Color pathColor = new(0.1f, 0.85f, 1f, 1f);
    [SerializeField] private Color waypointColor = new(1f, 0.85f, 0.1f, 1f);

    private readonly List<Waypoint> _waypoints = new();
    private int _waypointIndex;
    private Vector3 _lastWaypointAnchor;
    private Vector3 _target;
    private bool _hasPath;
    private bool _failed;

    private float _stuckTimer;
    private float _repathCooldownRemaining;
    private float _lastDistanceToWaypoint;
    private int _stuckRetryCount;

    // steer-only(_drivesTransform=false) 모드에서 외부가 읽는, 현재 따라가야 할 평면 이동 방향(정규화). 경로 없으면 zero.
    private Vector3 _desiredDirection;

    // 런타임 전용(직렬화 안 함). true면 이 에이전트가 transform을 직접 이동(잡몹/기본).
    // false면 경로/방향만 계산하고 DesiredDirection으로 노출(외부 mover가 이동). SetDrivesTransform로 토글.
    // [SerializeField]로 두면 이 필드가 없는 기존 프리팹이 deserialize 때 false로 떨어지는 함정이 있어 런타임 필드로 둔다.
    private bool _drivesTransform = true;

    private NavScratch _scratch;
    private NativeList<NavSpaceRef> _scratchNodes;
    private NativeList<NavPortal> _scratchPortals;
    private NativeList<float3> _scratchWaypoints;
    private bool _scratchInitialized;

    // 구독한 매니저를 캐시해 두어, OnDisable 시점에 Instance가 바뀌어도 정확히 해지(누수 방지).
    private SectorManager _subscribedManager;

    public bool HasPath => _hasPath;
    public bool Failed => _failed;
    public Vector3 Target => _target;

    // steer-only 모드에서 외부 mover가 읽는 평면 이동 방향(정규화). 경로 없으면 Vector3.zero.
    public Vector3 DesiredDirection => _desiredDirection;
    public void SetDrivesTransform(bool value) => _drivesTransform = value;
    public IReadOnlyList<Vector3> Waypoints
    {
        get
        {
            _publicWaypoints.Clear();
            for (int i = 0; i < _waypoints.Count; i++)
                _publicWaypoints.Add(_waypoints[i].Position);
            return _publicWaypoints;
        }
    }

    private readonly List<Vector3> _publicWaypoints = new();

    public void ConfigureAgent(float newMoveSpeed, float newAgentRadius, float newStopDistance)
    {
        moveSpeed = Mathf.Max(0.01f, newMoveSpeed);
        agentRadius = Mathf.Max(0f, newAgentRadius);
        stopDistance = Mathf.Max(0f, newStopDistance);

        if (_hasPath)
            RebuildPathToTarget();
    }

    private void Reset()
    {
        map = FindFirstObjectByType<MapNavigationAuthoring>();
    }

    private void OnEnable()
    {
        // Push 모델: 섹터 전환 통지를 구독한다. 단, 이 에이전트가 전환 이후에 스폰됐다면
        // 변경 이벤트는 이미 지나갔으므로 현재 섹터로 한 번 즉시 동기화(pull)한다.
        _subscribedManager = SectorManager.Instance;
        if (_subscribedManager == null)
            return;

        _subscribedManager.SectorChanged += OnSectorChanged;
        if (_subscribedManager.CurrentSector != null)
            SetMap(_subscribedManager.CurrentSector.NavAuthoring);
    }

    private void OnDisable()
    {
        if (_subscribedManager == null)
            return;

        _subscribedManager.SectorChanged -= OnSectorChanged;
        _subscribedManager = null;
    }

    private void OnDestroy()
    {
        DisposeScratch();
    }

    private void OnSectorChanged(Sector sector)
    {
        SetMap(sector != null ? sector.NavAuthoring : null);
    }

    // 현재 nav 맵을 교체한다. 진행 중이던 경로는 이전 섹터 그래프 기준이라 무효이므로 폐기하고,
    // 새 목표는 이 에이전트를 구동하는 상위 로직(장수 AI 등)이 다시 SetTarget으로 지정한다.
    public void SetMap(MapNavigationAuthoring newMap)
    {
        if (map == newMap)
            return;

        map = newMap;
        ClearPath();
    }

    private void EnsureScratchInitialized()
    {
        if (_scratchInitialized) return;
        _scratch = new NavScratch(64, Allocator.Persistent);
        _scratchNodes = new NativeList<NavSpaceRef>(16, Allocator.Persistent);
        _scratchPortals = new NativeList<NavPortal>(16, Allocator.Persistent);
        _scratchWaypoints = new NativeList<float3>(16, Allocator.Persistent);
        _scratchInitialized = true;
    }

    private void DisposeScratch()
    {
        if (!_scratchInitialized) return;
        if (_scratchWaypoints.IsCreated) _scratchWaypoints.Dispose();
        if (_scratchPortals.IsCreated) _scratchPortals.Dispose();
        if (_scratchNodes.IsCreated) _scratchNodes.Dispose();
        _scratch.Dispose();
        _scratchInitialized = false;
    }

    private void Update()
    {
        _repathCooldownRemaining = Mathf.Max(0f, _repathCooldownRemaining - Time.deltaTime);

        if (!_hasPath || _waypoints.Count == 0)
        {
            _desiredDirection = Vector3.zero;
            if (_drivesTransform)
                ApplyHeightSnap();
            return;
        }

        AdvancePastReachedWaypoints();
        if (_waypointIndex >= _waypoints.Count)
        {
            _desiredDirection = Vector3.zero;
            ClearPath(false);
            return;
        }

        Vector3 current = transform.position;
        Waypoint active = _waypoints[_waypointIndex];
        Vector3 planarDelta = active.Position - current;
        planarDelta.y = 0f;

        float reachDistance = NavAgentCore.GetReachDistance(stopDistance, waypointAdvanceDistance, active.Required);
        if (planarDelta.sqrMagnitude <= reachDistance * reachDistance)
        {
            _lastWaypointAnchor = active.Position;
            _waypointIndex++;
            ResetStuckTracking();
            return;
        }

        // 진행 정체(transform 기준) 감지 → 리패스. steer-only에서도 transform은 외부 mover가 움직이므로 유효하다.
        if (UpdateStuckProgress(planarDelta.magnitude))
            return;

        bool hasNext = _waypointIndex + 1 < _waypoints.Count;
        Vector3 nextWaypoint = hasNext ? _waypoints[_waypointIndex + 1].Position : current;
        Vector3 steeringTarget = NavAgentCore.GetSteeringTarget(current, active.Position, nextWaypoint, hasNext, cornerLookAheadDistance);
        Vector3 steering = NavAgentCore.ResolveSteering(steeringTarget, current, planarDelta);

        Vector3 direction = steering.normalized;

        // 외부 mover가 읽을 평면 방향을 항상 갱신한다.
        Vector3 planarDir = direction;
        planarDir.y = 0f;
        _desiredDirection = planarDir.sqrMagnitude > 0.0001f ? planarDir.normalized : Vector3.zero;

        // steer-only: 방향만 제공하고 transform은 건드리지 않는다(Character_MoveController 등이 실제 이동).
        if (!_drivesTransform)
            return;

        float stepDistance = Mathf.Min(moveSpeed * Time.deltaTime, planarDelta.magnitude);
        Vector3 next = current + direction * stepDistance;

        NavContext ctx = CreateContext();
        if (!NavAgentCore.CanMove(in ctx, current, next, active.Position, agentRadius, boundaryTolerance, reachDistance))
        {
            AccumulateBlockedStuck();
            ApplyHeightSnap();
            return;
        }

        transform.position = next;
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        ApplyHeightSnap();
    }

    public bool SetTarget(Vector3 worldTarget)
    {
        _target = worldTarget;
        _stuckRetryCount = 0;
        return RebuildPathToTarget();
    }

    public void ClearPath(bool failed = false)
    {
        _waypoints.Clear();
        _waypointIndex = 0;
        _hasPath = false;
        _failed = failed;
        _lastWaypointAnchor = transform.position;
        ResetStuckTracking();
    }

    private bool RebuildPathToTarget()
    {
        if (map == null)
        {
            ClearPath(true);
            return false;
        }

        BlobAssetReference<NavBlob> blob = ResolveBlob();
        if (!blob.IsCreated)
        {
            ClearPath(true);
            return false;
        }

        EnsureScratchInitialized();

        NavContext ctx = new NavContext(blob, map.transform.localToWorldMatrix, map.transform.worldToLocalMatrix);
        _scratchNodes.Clear();
        _scratchPortals.Clear();
        _scratchWaypoints.Clear();

        bool built = NavPath.TryBuild(
            in ctx,
            transform.position,
            _target,
            agentRadius,
            boundaryTolerance,
            ref _scratch,
            ref _scratchNodes,
            ref _scratchPortals,
            ref _scratchWaypoints);

        _waypoints.Clear();
        if (built)
        {
            for (int i = 0; i < _scratchWaypoints.Length; i++)
            {
                Vector3 p = _scratchWaypoints[i];
                _waypoints.Add(new Waypoint
                {
                    Position = p,
                    Required = NavAgentCore.IsTransitionWaypoint(in ctx, p, boundaryTolerance)
                });
            }
        }

        if (!built || _waypoints.Count == 0)
        {
            ClearPath(true);
            return false;
        }

        _waypointIndex = 0;
        _lastWaypointAnchor = transform.position;
        _hasPath = true;
        _failed = false;
        ResetStuckTracking();
        return true;
    }

    private void AdvancePastReachedWaypoints()
    {
        Vector3 current = transform.position;
        int startIndex = _waypointIndex;
        while (_waypointIndex < _waypoints.Count)
        {
            Waypoint waypoint = _waypoints[_waypointIndex];
            Vector3 delta = waypoint.Position - current;
            delta.y = 0f;

            float reach = NavAgentCore.GetReachDistance(stopDistance, waypointAdvanceDistance, waypoint.Required);
            if (delta.sqrMagnitude <= reach * reach)
            {
                _lastWaypointAnchor = waypoint.Position;
                _waypointIndex++;
                continue;
            }

            if (!waypoint.Required && NavAgentCore.HasPassedWaypoint(_lastWaypointAnchor, waypoint.Position, current))
            {
                _lastWaypointAnchor = waypoint.Position;
                _waypointIndex++;
                continue;
            }

            break;
        }

        if (_waypointIndex != startIndex)
            ResetStuckTracking();
    }

    private void ResetStuckTracking()
    {
        _stuckTimer = 0f;
        _lastDistanceToWaypoint = 0f;
    }

    private bool UpdateStuckProgress(float distanceToWaypoint)
    {
        if (NavAgentCore.EvaluateProgressStuck(
                ref _stuckTimer, ref _lastDistanceToWaypoint, _repathCooldownRemaining,
                distanceToWaypoint, stuckRepathDelay, stuckProgressDistance, moveSpeed, Time.deltaTime))
            return TriggerStuckRepath();

        return false;
    }

    private void AccumulateBlockedStuck()
    {
        if (NavAgentCore.EvaluateBlockedStuck(ref _stuckTimer, _repathCooldownRemaining, stuckRepathDelay, Time.deltaTime))
            TriggerStuckRepath();
    }

    // Bumps the per-target retry counter and either repaths or, once stuckRetryLimit is hit,
    // gives up (Failed). _stuckRetryCount is cleared only by SetTarget, so the limit bounds
    // total stuck repaths per target and prevents an endless repath loop.
    private bool TriggerStuckRepath()
    {
        bool giveUp = NavAgentCore.CommitStuckRepath(
            ref _stuckTimer, ref _repathCooldownRemaining, ref _stuckRetryCount,
            stuckRepathCooldown, stuckRetryLimit);

        if (giveUp)
            ClearPath(true);
        else
            RebuildPathToTarget();

        return true;
    }

    private void ApplyHeightSnap()
    {
        if (!snapHeight)
            return;

        NavContext ctx = CreateContext();
        if (NavAgentCore.TrySnapHeight(in ctx, transform.position, boundaryTolerance, heightOffset, out float3 snapped))
            transform.position = snapped;
    }

    private NavContext CreateContext()
    {
        if (map == null)
            return default;

        BlobAssetReference<NavBlob> blob = ResolveBlob();
        return blob.IsCreated
            ? new NavContext(blob, map.transform.localToWorldMatrix, map.transform.worldToLocalMatrix)
            : default;
    }

    // authoring.NavBlobData를 직접 읽으면 dirty 상태에서 dispose-rebuild를 트리거해 ECS 싱글톤 blob까지
    // dangling으로 만든다. 부트스트랩이 소유한 독립 blob을 공유받고, 부트스트랩 미초기화 시에만 폴백한다.
    private BlobAssetReference<NavBlob> ResolveBlob()
    {
        if (map == null)
            return default;

        NavRuntimeBootstrap boot = NavRuntimeBootstrap.Instance;
        if (boot != null)
        {
            BlobAssetReference<NavBlob> shared = boot.GetSharedBlob(map);
            if (shared.IsCreated)
                return shared;
        }

        return map.NavBlobData;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawPath || _waypoints.Count == 0)
            return;

        Gizmos.color = pathColor;
        Vector3 previous = transform.position;
        for (int i = _waypointIndex; i < _waypoints.Count; i++)
        {
            Gizmos.DrawLine(previous, _waypoints[i].Position);
            previous = _waypoints[i].Position;
        }

        Gizmos.color = waypointColor;
        for (int i = _waypointIndex; i < _waypoints.Count; i++)
            Gizmos.DrawSphere(_waypoints[i].Position, 0.08f);
    }

    private struct Waypoint
    {
        public Vector3 Position;
        public bool Required;
    }
}
