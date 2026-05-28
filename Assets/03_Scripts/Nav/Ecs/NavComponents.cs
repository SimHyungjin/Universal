using Unity.Collections;
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
        public float TargetRefreshDistance;
        public float TargetRefreshInterval;
        public float StuckRepathDelay;
        public float StuckRepathCooldown;
        public float StuckProgressDistance;
        public float SeparationRadius;
        public float SeparationStrength;
        public int SeparationMaxNeighbors;
        public int StuckRetryLimit; // 0 = unlimited, otherwise gives up to Failed after this many consecutive stuck repaths
        public float AttackDamage;
        public float AttackRange;
        public float AttackWindup;
        public float AttackCooldown;
        // 받는 데미지 감소율. CombatFormula.ReduceIncomingDamage(Defense, incoming).
        public float Defense;
    }

    public struct NavAgentTarget : IComponentData
    {
        public byte Dirty;
        public float3 Position;
        public float3 AcceptedPosition;
        public float RefreshCooldownRemaining;
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
        public byte   IsHeavy;
    }

    // 비주얼 전용 띄우기 트리거. ECS 시뮬 좌표는 평면 유지하며 NavVisualShell이 Version 변경을 감지해
    // 로컬에서 경과 시간을 추적하고 y 오프셋을 가산한다. 시뮬 시스템이 매 프레임 쓸 필드가 없도록 의도적으로 트리거만 둠.
    // 실제 y축 물리로 승격할 때 이 컴포넌트를 통째로 제거하고 NavAgentKnockback에 vertical velocity를 도입하면 폐기가 깔끔.
    public struct NavAgentLaunch : IComponentData
    {
        // ── 트리거 (HitboxProcessor가 새 launch 시 설정) ────────────────────
        public float Height;
        public float Duration;
        public float SuspendDuration;
        public byte  SuspendAtApex;
        // ── 시뮬레이션 상태 (NavLaunchSystem이 매 프레임 tick) ───────────────
        public float Elapsed;      // 발사 경과 시간
        public float FreezeTimer;  // >0이면 Elapsed 진행 정지 (타격마다 HitboxProcessor가 설정)
        // ── 출력 (NavLaunchSystem이 계산, Shell·HitboxProcessor가 읽음) ──────
        public float VisualYOffset;
    }

    public enum NavFaction : byte
    {
        Ally  = 0,
        Enemy = 1
    }

    public struct NavAgentFaction : IComponentData
    {
        public NavFaction Faction;
    }

    public struct NavAgentHealth : IComponentData
    {
        public float Max;
        public float Current;
    }

    public struct NavAgentDeath : IComponentData
    {
        public byte  Dying;
        public float Timer;
    }

    public enum NavAttackPhase : byte
    {
        Idle    = 0,
        Windup  = 1,
        Recover = 2
    }

    public struct NavAgentAttack : IComponentData
    {
        public NavAttackPhase Phase;
        public float Timer;
        public byte  HitPending;
    }

    // NavTargetingSystem이 찾아낸 현재 교전 대상. HasTarget이 0이면 적대 대상이 없는 상태.
    public struct NavAgentCombatTarget : IComponentData
    {
        public Entity TargetEntity;
        public float3 Position;
        public byte   HasTarget;
        public byte   IsPlayer;
    }

    public struct NavPathBuildBudget : IComponentData
    {
        public int MaxPathsPerFrame;
    }

    // 플레이어 위치를 ECS로 전달하는 싱글톤. MonoBehaviour 브릿지가 매 프레임 갱신한다.
    // HitRadius: 잡몹 공격 판정 시 플레이어를 점이 아닌 반경으로 취급해 1프레임 stale·넉백 미끄러짐을 흡수한다.
    public struct PlayerNavTarget : IComponentData
    {
        public float3 Position;
        public byte   HasValue;
        public float  HitRadius;
    }

    // SO_AttackData를 ECS 친화 raw 값으로 베이크한 잡몹 공격 프로파일.
    // NavRuntimeBootstrap이 스폰 시점에 SO_AttackData에서 값을 추출해 잡몹 entity에 부착한다.
    public struct NavAgentAttackProfile : IComponentData
    {
        public float Damage;
        public KnockbackType KnockbackType;
        public float KnockbackForce;
        public float KnockbackDuration;
        public float KnockbackFriction;
        public float HitstopDuration;
        public float HitstopTimeScale;
        public byte IsDownAttack;
        public float DownDuration;
        public AttackShape Shape;
        public float HitboxOffset;
        public float HitboxYOffset;
        public float HitboxVerticalTolerance;
        public float ShapeRadius;
        public float ShapeAngle;
        public float ShapeLength;
        public float ShapeWidth;
        public float SuperArmor;
        public float SuperArmorBreak;
        public HitType HitType;
        public SfxType HitSfx;
        public FixedString64Bytes HitVfxAddress;
        public FixedString64Bytes AttackStateName;
        public float AttackTransition;
    }

    // 적군의 공격 판정이 플레이어에 적중한 이벤트. PlayerNavTarget 싱글톤 엔티티에 버퍼로 부착.
    // NavAttackResolveSystem이 푸시하고, Player_HitReceiver가 매 프레임 드레인한다.
    public struct PlayerIncomingHit : IBufferElementData
    {
        public float3 SourcePosition;
        public NavAgentAttackProfile Attack;
    }
}
