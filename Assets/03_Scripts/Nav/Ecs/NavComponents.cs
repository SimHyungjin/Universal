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
        public float WakeupRecoveryDuration;
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
        public float  MotionLockTimer;
        public float  WakeupTimer;
        public float  Friction;
        public float  InitialSpeed;
        public int    HitType;
        public float  SuperArmorBreak;
        public int    HitVersion;
        public byte   IsHeavy;
    }

    // 실제 y축 부양. 잡몹 시뮬 좌표(LocalTransform.Position.y)를 실제로 띄운다.
    // NavLaunchSystem이 LaunchPhysics(초기속도+중력)로 y를 적분하고, NavMovementSystem은
    // Airborne 동안 height snap을 건너뛴다. 캐릭터(Character_ActionHandler)와 동일한 수직 물리 모델.
    public struct NavAgentLaunch : IComponentData
    {
        // ── 트리거 (HitboxProcessor가 새 launch 시 설정) ────────────────────
        public float Height;
        public float SuspendDuration;
        // ── 시뮬레이션 상태 (NavLaunchSystem이 매 프레임 tick) ───────────────
        public float VerticalVelocity; // 현재 수직 속도
        public float GroundY;          // launch 시작 시의 지면 y (착지 기준)
        public float SuspendTimer;     // 정점 체공 남은 시간
        public byte  Airborne;         // 1이면 공중 — NavMovementSystem이 height snap을 건너뜀
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
        public byte   IsCharacterTarget;
    }

    public struct NavPathBuildBudget : IComponentData
    {
        public int MaxPathsPerFrame;
    }

    // GameObject로 존재하는 캐릭터(플레이어/장수)를 ECS 잡몹이 타겟·타격하기 위한 다리.
    // 캐릭터 1명당 1엔티티(더 이상 싱글톤 아님). 같은 엔티티에 CharacterIncomingHit 버퍼가 붙는다.
    // MonoBehaviour 브릿지(Character_EcsBridge)가 매 프레임 위치/진영을 갱신한다.
    // HitRadius: 잡몹 공격 판정 시 캐릭터를 점이 아닌 반경으로 취급해 1프레임 stale·넉백 미끄러짐을 흡수한다.
    // Faction: 잡몹은 자신과 다른 진영의 캐릭터만 타겟·타격한다.
    public struct CharacterNavTarget : IComponentData
    {
        public float3     Position;
        public byte       HasValue;
        public float      HitRadius;
        public NavFaction Faction;
    }

    // SO_Attack_Data를 ECS 친화 raw 값으로 베이크한 잡몹 공격 프로파일.
    // NavRuntimeBootstrap이 스폰 시점에 SO_Attack_Data에서 값을 추출해 잡몹 entity에 부착한다.
    public struct NavAgentAttackProfile : IComponentData
    {
        public float Damage;
        public KnockbackType KnockbackType;
        public float KnockbackForce;
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

    // 적군의 공격 판정이 캐릭터에 적중한 이벤트. CharacterNavTarget 싱글톤 엔티티에 버퍼로 부착.
    // NavAttackResolveSystem이 푸시하고, Character_EcsHitReceiver가 매 프레임 드레인한다.
    public struct CharacterIncomingHit : IBufferElementData
    {
        public float3 SourcePosition;
        public NavAgentAttackProfile Attack;
    }
}
