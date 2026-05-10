using Unity.Entities;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class MapNavigationPathBuildSystem : SystemBase
{
    private readonly MapNavigationPathBuildResult _pathBuildResult = new();

    protected override void OnUpdate()
    {
        if (!SystemAPI.TryGetSingleton(out MapNavigationBlobComponent navigation) || !navigation.Blob.IsCreated)
            return;

        int remainingBudget = 16;
        if (SystemAPI.TryGetSingleton(out MapNavEcsPathBuildBudget budget))
            remainingBudget = Mathf.Max(1, budget.MaxPathsPerFrame);

        MapNavigationBlobDataContext blobContext = new(
            navigation.Blob,
            MapNavigationEcsConversion.ToMatrix4x4(navigation.LocalToWorldMatrix),
            MapNavigationEcsConversion.ToMatrix4x4(navigation.WorldToLocalMatrix));
        MapNavigationBlobPathContext pathContext = new(blobContext);

        foreach ((
            RefRW<MapNavEcsPathRequest> request,
            RefRO<MapNavEcsAgent> agent,
            RefRW<MapNavEcsMotionState> motion,
            RefRW<MapNavEcsPathStatus> status,
            DynamicBuffer<MapNavEcsWaypoint> waypoints)
            in SystemAPI.Query<
                    RefRW<MapNavEcsPathRequest>,
                    RefRO<MapNavEcsAgent>,
                    RefRW<MapNavEcsMotionState>,
                    RefRW<MapNavEcsPathStatus>,
                    DynamicBuffer<MapNavEcsWaypoint>>())
        {
            if (request.ValueRO.Pending == 0)
                continue;

            if (remainingBudget <= 0)
            {
                status.ValueRW.Waiting = 1;
                continue;
            }

            remainingBudget--;
            status.ValueRW.Waiting = 0;
            _pathBuildResult.Clear();
            waypoints.Clear();

            Vector3 startPosition = request.ValueRO.StartPosition;
            Vector3 actualStartPosition = request.ValueRO.ActualStartPosition;
            Vector3 targetPosition = request.ValueRO.TargetPosition;
            MapNavigationPathBuildSettings settings = new(
                Mathf.Max(0f, agent.ValueRO.AgentRadius),
                Mathf.Max(0f, agent.ValueRO.StopDistance),
                agent.ValueRO.UseRegionPathfinding != 0);
            MapNavigationPathBuildRequest buildRequest = new(startPosition, targetPosition, settings);
            MapNavigationEcsBlobPathAssembler assembler = new(
                pathContext,
                _pathBuildResult,
                settings.AgentRadius,
                settings.StopDistance,
                Mathf.Max(0f, agent.ValueRO.BoundaryTolerance),
                startPosition);

            bool built = MapNavigationPathBuilder.Build(
                pathContext,
                request.ValueRO.StartSpace,
                request.ValueRO.TargetSpace,
                buildRequest,
                startPosition,
                assembler,
                _pathBuildResult);

            if (built)
            {
                if (NeedsRecoveryWaypoint(actualStartPosition, startPosition, settings.StopDistance))
                {
                    waypoints.Add(new MapNavEcsWaypoint
                    {
                        Position = startPosition,
                        Required = 0
                    });
                }

                for (int i = 0; i < _pathBuildResult.Waypoints.Count; i++)
                {
                    MapNavWaypoint waypoint = _pathBuildResult.Waypoints[i];
                    waypoints.Add(new MapNavEcsWaypoint
                    {
                        Position = waypoint.Position,
                        Required = waypoint.Required ? (byte)1 : (byte)0
                    });
                }

                status.ValueRW.HasPath = (byte)(waypoints.Length > 0 ? 1 : 0);
                status.ValueRW.Waiting = 0;
                status.ValueRW.Failed = 0;
                status.ValueRW.UsedCrossLayerTransition = _pathBuildResult.UsedCrossLayerTransition ? (byte)1 : (byte)0;
                status.ValueRW.PathKind = GetPathKindCode(_pathBuildResult.PathKind);
                motion.ValueRW.WaypointIndex = 0;
                motion.ValueRW.LastWaypointAnchor = actualStartPosition;
                motion.ValueRW.LastDistanceToWaypoint = 0f;
            }
            else
            {
                status.ValueRW.HasPath = 0;
                status.ValueRW.Waiting = 0;
                status.ValueRW.Failed = 1;
                status.ValueRW.UsedCrossLayerTransition = 0;
                status.ValueRW.PathKind = default;
            }

            request.ValueRW.Pending = 0;
        }
    }

    private static int GetPathKindCode(string pathKind)
    {
        return pathKind switch
        {
            "R->R" => 1,
            "R->T" => 2,
            "T->R" => 3,
            "T->T internal" => 4,
            "T->T" => 5,
            _ => 0
        };
    }

    private static bool NeedsRecoveryWaypoint(Vector3 actualStart, Vector3 projectedStart, float stopDistance)
    {
        Vector3 delta = projectedStart - actualStart;
        delta.y = 0f;
        float threshold = Mathf.Max(0.0001f, stopDistance);
        return delta.sqrMagnitude > threshold * threshold;
    }
}
