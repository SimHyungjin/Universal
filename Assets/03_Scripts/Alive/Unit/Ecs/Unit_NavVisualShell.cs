using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace MapNav.Ecs
{
    [DisallowMultipleComponent]
    public sealed class Unit_NavVisualShell : MonoBehaviour
    {
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Animator animator;
        [SerializeField] private ActorAnimationTimeDomain animationTimeDomain = ActorAnimationTimeDomain.World;

        [Header("Faction Material")]
        [Tooltip("진영색을 입힐 본체 렌더러. 비우면 visualRoot 하위에서 자동 수집.")]
        [SerializeField] private Renderer[] bodyRenderers;

        [SerializeField] private bool syncPosition = true;
        [SerializeField] private bool syncRotation = true;
        [SerializeField] private float positionSharpness = 40f;
        [SerializeField] private float rotationSharpness = 40f;

        [Header("Debug")]
        [SerializeField] private bool drawSelectedPath = true;
        [SerializeField] private Color selectedPathColor = new Color(0.1f, 0.85f, 1f, 1f);
        [SerializeField] private Color selectedWaypointColor = new Color(1f, 0.85f, 0.1f, 1f);
        [SerializeField] private float selectedWaypointRadius = 0.12f;
        [SerializeField] private float selectedPathYOffset = 0.08f;

        private Entity _entity = Entity.Null;
        private NavFaction _faction;
        private SO_Unit_Data _unitData;
        private Renderer[] _resolvedBodyRenderers;
        private SO_Actor_AnimationData _boundAnimationData;
        private ActorLocomotionAnimation _locomotion;
        private int _attackHash;
        private HitReactionAnimSet _hitReactionSet;
        private float _previousMotionLockTimer;
        private int   _previousHitVersion;
        private byte  _previousLaunchAirborne;
        private bool  _downPendingAfterLaunch;
        private bool  _wasDownLocked;
        private bool _wasKnockedBack;
        private bool _wasDying;
        private bool _wasAttacking;
        private NavAttackPhase _previousAttackPhase;

        private Transform _root;
        private bool _animatorValid;
        private readonly ActorAnimationPlayback _animation = new();
        private float _cPositionSharpness, _cRotationSharpness;
        private float _cStartRunTransition, _cStopRunTransition, _cRunEnterDelay, _cMinRunDuration;
        private float _cAttackTransition;

        public Entity Entity => _entity;
        public NavFaction Faction => _faction;
        public bool IsBound => _entity != Entity.Null;

        private void Awake()
        {
            ResolveProfile();
            CacheHashes();
            BindAnimator();
        }

        private void OnValidate()
        {
            ResolveProfile();
            CacheHashes();
        }

        private void OnEnable()  => BindAnimator();
        private void OnDisable() => _animatorValid = false;

        private void OnDrawGizmosSelected()
        {
            if (!drawSelectedPath || _entity == Entity.Null)
                return;

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;

            EntityManager em = world.EntityManager;
            if (!em.Exists(_entity) || !em.HasComponent<NavAgentWaypoint>(_entity))
                return;

            Vector3 previous;
            if (em.HasComponent<LocalTransform>(_entity))
                previous = em.GetComponentData<LocalTransform>(_entity).Position;
            else
                previous = (visualRoot != null ? visualRoot : transform).position;

            previous.y += selectedPathYOffset;

            DynamicBuffer<NavAgentWaypoint> waypoints = em.GetBuffer<NavAgentWaypoint>(_entity, true);
            int startIndex = 0;
            if (em.HasComponent<NavAgentMotion>(_entity))
            {
                NavAgentMotion motion = em.GetComponentData<NavAgentMotion>(_entity);
                startIndex = Mathf.Clamp(motion.WaypointIndex, 0, waypoints.Length);
            }

            Gizmos.color = selectedPathColor;
            for (int i = startIndex; i < waypoints.Length; i++)
            {
                Vector3 waypoint = waypoints[i].Position;
                waypoint.y += selectedPathYOffset;
                Gizmos.DrawLine(previous, waypoint);
                previous = waypoint;
            }

            Gizmos.color = selectedWaypointColor;
            for (int i = startIndex; i < waypoints.Length; i++)
            {
                Vector3 waypoint = waypoints[i].Position;
                waypoint.y += selectedPathYOffset;
                Gizmos.DrawSphere(waypoint, selectedWaypointRadius);
            }
        }

        public void Bind(Entity entity, NavFaction faction, in LocalTransform initialTransform, in NavAgentAttackProfile attackProfile, SO_Unit_Data unitData)
        {
            _entity = entity;
            _faction = faction;
            _unitData = unitData;
            _boundAnimationData = unitData != null ? unitData.AnimationData : null;
            ApplyFactionMaterial(faction);
            ResolveProfile();
            CacheHashes();
            _animation.Reset();
            _previousMotionLockTimer = 0f;
            _previousHitVersion = 0;
            _previousLaunchAirborne = 0;
            _downPendingAfterLaunch = false;
            _wasDownLocked = false;
            _wasKnockedBack = false;
            _wasDying = false;
            _wasAttacking = false;
            _previousAttackPhase = NavAttackPhase.Idle;

            // 잡몹의 공격 모션 정보는 SO_Attack_Data가 단일 진실이며, 스폰 시점에 NavAgentAttackProfile로 베이크되어 들어온다.
            string attackStateName = attackProfile.AttackStateName.IsEmpty
                ? null
                : attackProfile.AttackStateName.ToString();
            _attackHash = string.IsNullOrWhiteSpace(attackStateName)
                ? 0
                : Animator.StringToHash(attackStateName);
            _animation.RegisterStateName(attackStateName);
            _cAttackTransition = attackProfile.AttackTransition;

            _root.SetPositionAndRotation(initialTransform.Position, initialTransform.Rotation);
        }

        public void Unbind()
        {
            _entity = Entity.Null;
            _wasKnockedBack = false;
            _wasDying = false;
            _wasAttacking = false;
            _previousAttackPhase = NavAttackPhase.Idle;
            _previousMotionLockTimer = 0f;
            _previousHitVersion = 0;
            _previousLaunchAirborne = 0;
            _downPendingAfterLaunch = false;
            _wasDownLocked = false;
            _boundAnimationData = null;
            if (CanUseAnimator())
                ApplyAnim(false);
        }

        public void Tick(
            in LocalTransform ecsTransform,
            in NavAgentMotion motion,
            in NavAgentKnockback knockback,
            in NavAgentLaunch launch,
            in NavAgentDeath death,
            in NavAgentAttack attack,
            NavFaction faction,
            float deltaTime)
        {
            Transform root = _root;
            // 잡몹 y가 실제로 뜨므로(NavLaunchSystem) 비주얼은 시뮬 좌표를 그대로 따라간다.
            if (syncPosition)
                root.position = Damp(root.position, ecsTransform.Position, _cPositionSharpness, deltaTime);
            if (syncRotation)
                root.rotation = Quaternion.Slerp(root.rotation, ecsTransform.Rotation, DampFactor(_cRotationSharpness, deltaTime));

            bool dying = death.Dying != 0;
            // NavDeathSystem이 쓰러짐(death) 타이머를 끝내며 Dying을 푸는 순간 = 아군 부활.
            // ECS가 같은 프레임에 faction=Ally로 바꿔 넘겨주므로, 여기서 파티클·머테리얼·wakeup을 한 번에 연출한다.
            bool reviving = _wasDying && !dying;
            if (CanUseAnimator())
                _animation.SyncSpeed(animationTimeDomain);

            if (dying && !_wasDying)
            {
                if (CanUseAnimator())
                    _animation.PlayHitReaction(_hitReactionSet, HitReactionKind.Death);
                _animation.ResetLocomotionTimers();
            }
            else if (reviving)
            {
                // 사망→부활: 파티클 발생 → 머테리얼 변경(ConvertTo 내부 순서) → wakeup 애니메이션.
                ConvertTo(faction);
                if (CanUseAnimator())
                    _animation.PlayHitReaction(_hitReactionSet, HitReactionKind.Wakeup);
                _animation.ResetLocomotionTimers();
            }
            else if (!dying && faction != _faction)
            {
                // 살아있는 채 진영이 바뀐 변이(매크로 비율 조정): 파티클 + 머테리얼만, wakeup 없음.
                ConvertTo(faction);
            }

            bool attacking = !dying && attack.Phase != NavAttackPhase.Idle;
            bool startingAttack = !dying
                && attack.Phase == NavAttackPhase.Windup
                && _previousAttackPhase != NavAttackPhase.Windup;
            if ((attacking && !_wasAttacking) || startingAttack)
            {
                _animation.ForcePlay(_attackHash, _cAttackTransition);
                _animation.ResetLocomotionTimers();
            }

            bool isKnockedBack = NavKnockbackSystem.HasPlanarKnockbackVelocity(knockback.Velocity);
            bool isLocked      = knockback.MotionLockTimer > 0f;
            bool isWakingUp    = knockback.WakeupTimer > 0f;
            bool isAirborne    = launch.Airborne != 0;
            bool landed        = _previousLaunchAirborne != 0 && !isAirborne;
            bool playedReactionThisTick = false;

            // 사망 중에는 피격 리액션 애니메이션으로 사망 애니메이션을 덮어쓰지 않는다.
            // 부활 프레임(reviving)도 제외: MotionLockTimer가 wakeup 값으로 점프해 피격으로 오인되어
            // wakeup 애니메이션을 덮어쓰는 것을 막는다.
            if (!dying && !reviving)
            {
                bool newKnockback  = isKnockedBack && !_wasKnockedBack;
                bool newMotionLock = knockback.MotionLockTimer > _previousMotionLockTimer + 0.0001f;
                bool newHitVersion = knockback.HitVersion != _previousHitVersion && knockback.HitVersion != 0;

                if (newKnockback || newMotionLock || newHitVersion)
                {
                    // 잡몹 IsHeavy는 down 공격 표식 → HeavyHit, 그 외 LightHit. (캐릭터와 동일한 진입·실행 메커니즘)
                    bool downHit = knockback.IsHeavy != 0 && isLocked;
                    HitReactionKind kind;
                    if (isAirborne)
                    {
                        kind = HitReactionKind.Launch;
                        _downPendingAfterLaunch = downHit;
                    }
                    else if (downHit)
                    {
                        kind = HitReactionKind.Down;
                        _wasDownLocked = true;
                    }
                    else
                    {
                        kind = knockback.IsHeavy != 0 ? HitReactionKind.HeavyHit : HitReactionKind.LightHit;
                    }
                    if (CanUseAnimator())
                        _animation.PlayHitReaction(_hitReactionSet, kind);
                    _animation.ResetLocomotionTimers();
                    playedReactionThisTick = true;
                }

                if (landed && _downPendingAfterLaunch && isLocked)
                {
                    _downPendingAfterLaunch = false;
                    _wasDownLocked = true;
                    if (CanUseAnimator())
                        _animation.PlayHitReaction(_hitReactionSet, HitReactionKind.Down);
                    _animation.ResetLocomotionTimers();
                    playedReactionThisTick = true;
                }
                else if (landed)
                {
                    _downPendingAfterLaunch = false;
                }

                if (_wasDownLocked && _previousMotionLockTimer > 0f && !isLocked && !isAirborne)
                {
                    _wasDownLocked = false;
                    if (CanUseAnimator())
                        _animation.PlayHitReaction(_hitReactionSet, HitReactionKind.Wakeup);
                    _animation.ResetLocomotionTimers();
                    playedReactionThisTick = true;
                }
            }

            if (!dying && !attacking && !isLocked && !isWakingUp && !isAirborne && !playedReactionThisTick)
                ApplyAnim(motion.IsMoving != 0);

            _wasDying = dying;
            _wasAttacking = attacking;
            _previousAttackPhase = attack.Phase;
            _wasKnockedBack = isKnockedBack;
            _previousLaunchAirborne = launch.Airborne;
            _previousMotionLockTimer = knockback.MotionLockTimer;
            _previousHitVersion = knockback.HitVersion;
        }

        public void TickIdle()
        {
            ApplyAnim(false);
        }

        private void ApplyAnim(bool moving)
        {
            if (!CanUseAnimator()) return;
            float animationDeltaTime = Time.unscaledDeltaTime * ActorAnimationPlayback.ResolveSpeed(animationTimeDomain);
            _animation.TickLocomotion(moving, animationDeltaTime, _locomotion);
        }

        private bool CanUseAnimator() => _animatorValid;

        private void BindAnimator()
        {
            _animation.Bind(animator);
            _animatorValid = _animation.IsValid;

            // 잡몹 비주얼의 위치는 ECS(NavLaunchSystem의 y + Tick의 syncPosition)가 전적으로 제어한다.
            // Apply Root Motion이 켜져 있으면 launch/down 히트 리액션 클립의 루트 모션이 모델을 땅 아래로
            // 끌어내려 syncPosition과 충돌한다(launch 당한 잡몹이 반쯤 땅에 박힘). 비주얼 셸에서는 항상 끈다.
            if (animator != null)
                animator.applyRootMotion = false;
        }

        private void ResolveProfile()
        {
            _root = visualRoot != null ? visualRoot : transform;
            SO_Actor_AnimationData data = ActiveAnimationData;
            _cPositionSharpness       = positionSharpness;
            _cRotationSharpness       = rotationSharpness;
            _cStartRunTransition      = data != null ? data.StartRunTransition : 0.05f;
            _cStopRunTransition       = data != null ? data.StopRunTransition : 0.18f;
            _cRunEnterDelay           = data != null ? data.RunEnterDelay : 0.06f;
            _cMinRunDuration          = data != null ? data.MinRunDuration : 0.18f;
            // 피격 반응은 캐릭터와 동일한 공유 HitReactionPlayer가 재생한다(잡몹은 down/launch/wakeup 클립이
            // 없어 fallback=heavy로 동작). 명명/세분화 규칙은 HitReactionAnimSet 한 곳에 있다.
            _hitReactionSet           = data != null ? data.BuildHitReactionSet() : default;
        }

        private void CacheHashes()
        {
            SO_Actor_AnimationData data = ActiveAnimationData;
            string idleState = data != null ? data.IdleStateName : "Idle";
            string runState  = data != null ? data.RunStateName  : "Run";
            _locomotion = ActorLocomotionAnimation.FromStateNames(
                idleState,
                runState,
                _cStartRunTransition,
                _cStopRunTransition,
                _cRunEnterDelay,
                _cMinRunDuration);
            _animation.RegisterStateNames(idleState, runState);
        }

        private SO_Actor_AnimationData ActiveAnimationData => _boundAnimationData;

        // ── 진영 머테리얼 / 전향 ──────────────────────────────────────────────────
        // 본체 렌더러에 진영색을 입힌다. Bind(스폰)와 ConvertTo(전향)가 공용으로 쓴다.
        private void ApplyFactionMaterial(NavFaction faction)
        {
            if (_unitData == null) return;
            Material mat = _unitData.MaterialFor(faction);
            if (mat == null) return;

            Renderer[] renderers = ResolveBodyRenderers();
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].sharedMaterial = mat;
        }

        private Renderer[] ResolveBodyRenderers()
        {
            if (bodyRenderers != null && bodyRenderers.Length > 0) return bodyRenderers;
            _resolvedBodyRenderers ??= (visualRoot != null ? visualRoot : transform).GetComponentsInChildren<Renderer>(true);
            return _resolvedBodyRenderers;
        }

        // 적↔아군 전향: 그 자리에 전향 파티클을 터뜨린 뒤 진영 머테리얼을 교체한다(파티클 → 머테리얼 순).
        // 죽음→부활(wakeup 동반)과 살아있는 변이 양쪽에서 Tick이 호출한다.
        // 파티클은 풀(CombatFeedback→App.Spawn)에서 스폰돼 AutoDespawn이 자동 회수한다(Instantiate 누수 금지).
        public void ConvertTo(NavFaction faction)
        {
            _faction = faction;
            if (_unitData != null)
                CombatFeedback.SpawnVfxAtPosition(_unitData.ConversionVfxAddress, _root.position, destroyCancellationToken);
            ApplyFactionMaterial(faction);
        }

        private static Vector3 Damp(Vector3 current, Vector3 target, float sharpness, float deltaTime)
            => Vector3.Lerp(current, target, DampFactor(sharpness, deltaTime));

        private static float DampFactor(float sharpness, float deltaTime)
        {
            if (sharpness <= 0f) return 1f;
            return 1f - math.exp(-sharpness * deltaTime);
        }
    }
}
