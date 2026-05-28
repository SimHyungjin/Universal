using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace MapNav.Ecs
{
    [DisallowMultipleComponent]
    public sealed class NavVisualShell : MonoBehaviour
    {
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Animator animator;
        [SerializeField] private SO_UnitAnimationData animationData;

        [SerializeField] private bool syncPosition = true;
        [SerializeField] private bool syncRotation = true;

        [Header("Health Bar")]
        [SerializeField] private GameObject healthBarRoot;
        [SerializeField] private Renderer healthFillRenderer;
        [SerializeField] private bool faceHealthBarToCamera = true;

        [Header("Debug")]
        [SerializeField] private bool drawSelectedPath = true;
        [SerializeField] private Color selectedPathColor = new Color(0.1f, 0.85f, 1f, 1f);
        [SerializeField] private Color selectedWaypointColor = new Color(1f, 0.85f, 0.1f, 1f);
        [SerializeField] private float selectedWaypointRadius = 0.12f;
        [SerializeField] private float selectedPathYOffset = 0.08f;

        private Entity _entity = Entity.Null;
        private NavFaction _faction;
        private Camera _cachedCamera;
        private Transform _cachedCameraTransform;
        private int _idleHash, _runHash, _currentHash;
        private int _hitLightHash, _hitHeavyHash, _deathHash, _attackHash;
        private float _movingTime, _runTime;
        private float _previousKnockbackTimer;
        private float _previousMotionLockTimer;
        private int   _previousHitVersion;
        private int   _previousLaunchVersion;
        private float _launchElapsed;
        private float _launchHeight;
        private float _launchDuration;
        private float _launchSuspendDuration;
        private int   _previousFreezeVersion;
        private float _freezeTimer;
        public  float VisualYOffset { get; private set; }
        private bool _wasKnockedBack;
        private bool _wasDying;
        private bool _wasAttacking;
        private NavAttackPhase _previousAttackPhase;

        private Transform _root;
        private bool _animatorValid;
        private float _cPositionSharpness, _cRotationSharpness;
        private float _cStartRunTransition, _cStopRunTransition, _cRunEnterDelay, _cMinRunDuration;
        private float _cHitTransition;
        private string _cHitLightStateName, _cHitHeavyStateName, _cHitDeathStateName;
        private float _cAttackTransition;
        private MaterialPropertyBlock _healthFillBlock;
        private float _previousHealthFill = -1f;
        private bool _healthBarVisible;

        private static readonly int FillId = Shader.PropertyToID("_Fill");

        public Entity Entity => _entity;
        public NavFaction Faction => _faction;
        public bool IsBound => _entity != Entity.Null;

        private void Awake()
        {
            ResolveProfile();
            CacheHashes();
            _animatorValid = animator != null;
        }

        private void OnValidate()
        {
            ResolveProfile();
            CacheHashes();
        }

        private void OnEnable()  => _animatorValid = animator != null;
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

        public void Bind(Entity entity, NavFaction faction, in LocalTransform initialTransform, in NavAgentAttackProfile attackProfile)
        {
            _entity = entity;
            _faction = faction;
            _currentHash = 0;
            _movingTime = 0f;
            _runTime = 0f;
            _previousKnockbackTimer = 0f;
            _previousMotionLockTimer = 0f;
            _previousHitVersion = 0;
            _previousLaunchVersion = 0;
            _launchElapsed = 0f;
            _launchHeight = 0f;
            _launchDuration = 0f;
            _previousFreezeVersion = 0;
            _freezeTimer = 0f;
            _wasKnockedBack = false;
            _wasDying = false;
            _wasAttacking = false;
            _previousAttackPhase = NavAttackPhase.Idle;
            _previousHealthFill = -1f;
            SetHealthBarVisible(false);

            // 잡몹의 공격 모션 정보는 SO_AttackData가 단일 진실이며, 스폰 시점에 NavAgentAttackProfile로 베이크되어 들어온다.
            _attackHash = attackProfile.AttackStateName.IsEmpty
                ? 0
                : Animator.StringToHash(attackProfile.AttackStateName.ToString());
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
            _previousKnockbackTimer = 0f;
            _previousMotionLockTimer = 0f;
            _previousHitVersion = 0;
            _previousLaunchVersion = 0;
            _launchElapsed = 0f;
            _launchHeight = 0f;
            _launchDuration = 0f;
            _launchSuspendDuration = 0f;
            _previousFreezeVersion = 0;
            _freezeTimer = 0f;
            _previousHealthFill = -1f;
            SetHealthBarVisible(false);
            if (CanUseAnimator())
                ApplyAnim(false);
        }

        public void Tick(
            in LocalTransform ecsTransform,
            in NavAgentMotion motion,
            in NavAgentKnockback knockback,
            in NavAgentDeath death,
            in NavAgentAttack attack,
            in NavAgentHealth health,
            in NavAgentLaunch launch,
            float deltaTime)
        {
            Transform root = _root;
            if (syncPosition)
                root.position = Damp(root.position, ecsTransform.Position, _cPositionSharpness, deltaTime);
            if (syncRotation)
                root.rotation = Quaternion.Slerp(root.rotation, ecsTransform.Rotation, DampFactor(_cRotationSharpness, deltaTime));

            ApplyLaunchOffset(launch, ecsTransform.Position.y, deltaTime);

            bool dying = death.Dying != 0;
            UpdateHealthBar(health, dying);

            if (dying && !_wasDying)
            {
                if (_deathHash != 0) ForcePlay(_deathHash, _cHitTransition);
                _movingTime = 0f;
                _runTime = 0f;
            }

            bool attacking = !dying && attack.Phase != NavAttackPhase.Idle;
            bool startingAttack = !dying
                && attack.Phase == NavAttackPhase.Windup
                && _previousAttackPhase != NavAttackPhase.Windup;
            if ((attacking && !_wasAttacking) || startingAttack)
            {
                if (_attackHash != 0) ForcePlay(_attackHash, _cAttackTransition);
                _movingTime = 0f;
                _runTime = 0f;
            }

            bool isKnockedBack = knockback.Timer > 0f;
            bool isLocked      = knockback.MotionLockTimer > 0f;

            // 사망 중에는 피격 리액션 애니메이션으로 사망 애니메이션을 덮어쓰지 않는다.
            if (!dying)
            {
                bool newKnockback  = isKnockedBack && (!_wasKnockedBack || knockback.Timer > _previousKnockbackTimer + 0.0001f);
                bool newMotionLock = knockback.MotionLockTimer > _previousMotionLockTimer + 0.0001f;
                bool newHitVersion = knockback.HitVersion != _previousHitVersion && knockback.HitVersion != 0;

                if (newKnockback || newMotionLock || newHitVersion)
                {
                    bool heavy = knockback.IsHeavy != 0;
                    int hash = heavy ? _hitHeavyHash : _hitLightHash;
                    if (hash != 0) ForcePlay(hash, _cHitTransition);
                    _movingTime = 0f;
                    _runTime = 0f;
                }
            }

            if (!dying && !attacking && !isLocked)
                ApplyAnim(motion.IsMoving != 0);

            _wasDying = dying;
            _wasAttacking = attacking;
            _previousAttackPhase = attack.Phase;
            _wasKnockedBack = isKnockedBack;
            _previousKnockbackTimer = knockback.Timer;
            _previousMotionLockTimer = knockback.MotionLockTimer;
            _previousHitVersion = knockback.HitVersion;
        }

        public void TickIdle()
        {
            SetHealthBarVisible(false);
            ApplyAnim(false);
        }

        private void ApplyLaunchOffset(in NavAgentLaunch launch, float baseY, float deltaTime)
        {
            if (launch.Version != _previousLaunchVersion)
            {
                _previousLaunchVersion = launch.Version;
                _launchElapsed = 0f;
                _launchHeight = launch.Height;
                _launchDuration = launch.Duration;
                _launchSuspendDuration = launch.SuspendDuration;
            }

            if (_launchDuration <= 0f || _launchHeight <= 0f) { VisualYOffset = 0f; return; }
            float totalDuration = launch.SuspendAtApex != 0
                ? Mathf.Max(_launchDuration, _launchSuspendDuration)
                : _launchDuration;
            if (_launchElapsed >= totalDuration) { VisualYOffset = 0f; return; }

            if (launch.FreezeVersion != _previousFreezeVersion)
            {
                _previousFreezeVersion = launch.FreezeVersion;
                _freezeTimer = launch.FreezeDuration;
            }

            if (_freezeTimer > 0f)
                _freezeTimer -= deltaTime;
            else
                _launchElapsed += deltaTime;

            float t = GetLaunchCurveT(_launchElapsed, _launchDuration, totalDuration, launch.SuspendAtApex != 0);
            // 표준 포물선: t=0.5에서 최고점 height. AnimationCurve가 필요해지면 SO_AttackData.Launch에 추가.
            float yOffset = 4f * _launchHeight * t * (1f - t);
            VisualYOffset = yOffset;

            // 절대값 설정. Damp가 hitstop 등으로 거의 안 움직일 때 가산 방식이 누적되어 위로 솟구치는 버그를 막는다.
            Transform root = _root;
            Vector3 pos = root.position;
            pos.y = baseY + yOffset;
            root.position = pos;
        }

        private static float GetLaunchCurveT(float elapsed, float arcDuration, float totalDuration, bool suspendAtApex)
        {
            if (!suspendAtApex)
                return Mathf.Clamp01(elapsed / arcDuration);

            float halfArcDuration = arcDuration * 0.5f;
            if (elapsed < halfArcDuration)
                return Mathf.Clamp01(elapsed / arcDuration);

            float fallStart = Mathf.Max(halfArcDuration, totalDuration - halfArcDuration);
            if (elapsed < fallStart)
                return 0.5f;

            return Mathf.Clamp01(0.5f + (elapsed - fallStart) / arcDuration);
        }

        private void ApplyAnim(bool moving)
        {
            if (!CanUseAnimator()) return;
            if (_idleHash == 0 || _runHash == 0) return;

            float deltaTime = Time.deltaTime;
            if (moving)
            {
                _movingTime += deltaTime;
                _runTime += deltaTime;
                if (_currentHash != _runHash && _movingTime >= _cRunEnterDelay)
                {
                    _runTime = 0f;
                    Play(_runHash, _cStartRunTransition);
                }
                return;
            }
            _movingTime = 0f;
            if (_currentHash == _runHash)
            {
                _runTime += deltaTime;
                if (_runTime < _cMinRunDuration) return;
            }
            Play(_idleHash, _cStopRunTransition);
        }

        private void Play(int stateHash, float transitionDuration)
        {
            if (!CanUseAnimator()) return;
            if (_currentHash == stateHash) return;
            _currentHash = stateHash;
            animator.CrossFade(stateHash, transitionDuration);
        }

        private void ForcePlay(int stateHash, float transitionDuration)
        {
            if (!CanUseAnimator()) return;
            _currentHash = stateHash;
            animator.CrossFade(stateHash, transitionDuration, 0, 0f);
        }

        private bool CanUseAnimator() => _animatorValid;

        private void UpdateHealthBar(in NavAgentHealth health, bool dying)
        {
            if (healthBarRoot == null || healthFillRenderer == null)
                return;

            bool visible = !dying && health.Max > 0f && health.Current > 0f && health.Current < health.Max;
            SetHealthBarVisible(visible);
            if (!visible)
                return;

            float fill = Mathf.Clamp01(health.Current / health.Max);
            if (!Mathf.Approximately(fill, _previousHealthFill))
            {
                _healthFillBlock ??= new MaterialPropertyBlock();
                healthFillRenderer.GetPropertyBlock(_healthFillBlock);
                _healthFillBlock.SetFloat(FillId, fill);
                healthFillRenderer.SetPropertyBlock(_healthFillBlock);
                _previousHealthFill = fill;
            }

            if (!faceHealthBarToCamera)
                return;

            Transform cam = ResolveMainCameraTransform();
            if (cam == null)
                return;

            Transform bar = healthBarRoot.transform;
            bar.rotation = Quaternion.LookRotation(cam.forward, cam.up);
        }

        private Transform ResolveMainCameraTransform()
        {
            if (_cachedCamera != null && _cachedCamera.isActiveAndEnabled)
                return _cachedCameraTransform;

            _cachedCamera = Camera.main;
            _cachedCameraTransform = _cachedCamera != null ? _cachedCamera.transform : null;
            return _cachedCameraTransform;
        }

        private void SetHealthBarVisible(bool visible)
        {
            if (_healthBarVisible == visible)
                return;

            _healthBarVisible = visible;
            if (healthBarRoot != null)
                healthBarRoot.SetActive(visible);
        }

        private void ResolveProfile()
        {
            _root = visualRoot != null ? visualRoot : transform;
            if (animationData == null) return;

            _cPositionSharpness       = animationData.PositionSharpness;
            _cRotationSharpness       = animationData.RotationSharpness;
            _cStartRunTransition      = animationData.StartRunTransition;
            _cStopRunTransition       = animationData.StopRunTransition;
            _cRunEnterDelay           = animationData.RunEnterDelay;
            _cMinRunDuration          = animationData.MinRunDuration;
            _cHitTransition           = animationData.HitTransition;
            _cHitLightStateName       = animationData.LightStateName;
            _cHitHeavyStateName       = animationData.HeavyStateName;
            _cHitDeathStateName       = animationData.DeathStateName;
        }

        private void CacheHashes()
        {
            string idleState = animationData != null ? animationData.IdleStateName : "Idle";
            string runState  = animationData != null ? animationData.RunStateName  : "Run";
            _idleHash     = string.IsNullOrWhiteSpace(idleState)          ? 0 : Animator.StringToHash(idleState);
            _runHash      = string.IsNullOrWhiteSpace(runState)           ? 0 : Animator.StringToHash(runState);
            _hitLightHash = string.IsNullOrWhiteSpace(_cHitLightStateName) ? 0 : Animator.StringToHash(_cHitLightStateName);
            _hitHeavyHash = string.IsNullOrWhiteSpace(_cHitHeavyStateName) ? 0 : Animator.StringToHash(_cHitHeavyStateName);
            _deathHash    = string.IsNullOrWhiteSpace(_cHitDeathStateName) ? 0 : Animator.StringToHash(_cHitDeathStateName);
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
