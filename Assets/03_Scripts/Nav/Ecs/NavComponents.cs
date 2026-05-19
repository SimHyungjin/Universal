using Unity.Entities;
using Unity.Mathematics;

namespace MapNav.Ecs
{
    public struct NavAgentSettings : IComponentData
    {
        public float AgentRadius;
        public float StopDistance;
        public float MoveSpeed;
        public float WaypointAdvanceDistance;
        public float CornerLookAheadDistance;
        public float HeightOffset;
        public float BoundaryTolerance;
        public float TargetRepathDistance;
        public float StuckRepathDelay;
        public float StuckRepathCooldown;
        public float StuckProgressDistance;
        public float SeparationRadius;
        public float SeparationStrength;
        public int SeparationMaxNeighbors;
        public int StuckRetryLimit; // 0 = unlimited, otherwise gives up to Failed after this many consecutive stuck repaths
    }

    public struct NavAgentTarget : IComponentData
    {
        public byte Dirty;
        public float3 Position;
        public float3 AcceptedPosition;
    }

    public struct NavAgentTargetCommand : IComponentData
    {
        public float3 Position;
    }

    public struct NavAgentPathRequest : IComponentData
    {
        public byte Pending;
        public float3 StartWorld;
        public float3 ActualStartWorld;
        public float3 TargetWorld;
    }

    public struct NavAgentPathStatus : IComponentData
    {
        public byte HasPath;
        public byte Waiting;
        public byte Failed;
    }

    public struct NavAgentMotion : IComponentData
    {
        public byte IsMoving;
        public int WaypointIndex;
        public float CurrentSpeed;
        public float StuckTimer;
        public float RepathCooldownRemaining;
        public float LastDistanceToWaypoint;
        public float3 LastWaypointAnchor;
        public float3 Velocity;
        public int StuckRetryCount; // Reset on new external target command or successful path; bumped on each stuck repath
    }

    public struct NavAgentWaypoint : IBufferElementData
    {
        public float3 Position;
        public byte Required;
    }

    public struct NavAgentSeparation : IComponentData
    {
        public float3 Steering;
        public int NeighborCount;
    }

    public struct NavAgentKnockback : IComponentData
    {
        public float3 Velocity;
        public float  Timer;
        public float  MotionLockTimer;
        public float  Friction;
        public float  InitialSpeed;
        public int    HitType;
        public float  SuperArmorBreak;
        public int    HitVersion;
    }

    public struct NavPathBuildBudget : IComponentData
    {
        public int MaxPathsPerFrame;
    }
}
