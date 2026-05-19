using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace MapNav.Ecs
{
    [DisallowMultipleComponent]
    public sealed class NavVisualShell : MonoBehaviour
    {
        private enum AnimMode { CrossFade = 0, Parameters = 1 }

        [SerializeField] private Transform visualRoot;
        [SerializeField] private Animator animator;
        [SerializeField] private AnimMode animationMode = AnimMode.CrossFade;
        [SerializeField] private SO_NavVisualProfile visualProfile;
        [SerializeField] private SO_HitReactionProfile hitReactionProfile;

        [SerializeField] private bool syncPosition = true;
        [SerializeField] private bool syncRotation = true;

        [Header("Legacy Fallbacks")]
        [SerializeField, HideInInspector] private string idleStateName = "Idle";
        [SerializeField, HideInInspector] private string runStateName = "Run";
        [SerializeField, HideInInspector] private float startRunTransition = 0.05f;
        [SerializeField, HideInInspector] private float stopRunTransition = 0.18f;
        [SerializeField, HideInInspector] private float runEnterDelay = 0.06f;
        [SerializeField, HideInInspector] private float minRunDuration = 0.18f;
        [SerializeField, HideInInspector] private string speedParameter = "Speed";
        [SerializeField, HideInInspector] private string movingParameter = "Moving";
        [SerializeField, HideInInspector] private float positionSharpness = 40f;
        [SerializeField, HideInInspector] private float rotationSharpness = 40f;
        [SerializeField, HideInInspector] private string hitLightStateName = "HitLight";
        [SerializeField, HideInInspector] private string hitHeavyStateName = "HitHeavy";
        [SerializeField, HideInInspector] private float  hitHeavyThreshold = 8f;
        [SerializeField, HideInInspector] private float  hitTransition     = 0.05f;
        [SerializeField, HideInInspector, Range(0.1f, 1f)] private float hitLockClipRatio = 0.45f;
        [SerializeField, HideInInspector] private float  hitPostAnimationHold = 0.1f;
        [SerializeField, HideInInspector] private float  hitMaxLockDuration = 0.75f;
        [SerializeField, HideInInspector] private float  hitFallbackLockDuration = 0.5f;

        [Header("Debug")]
        [SerializeField] private bool drawSelectedPath = true;
        [SerializeField] private Color selectedPathColor = new Color(0.1f, 0.85f, 1f, 1f);
        [SerializeField] private Color selectedWaypointColor = new Color(1f, 0.85f, 0.1f, 1f);
        [SerializeField] private float selectedWaypointRadius = 0.12f;
        [SerializeField] private float selectedPathYOffset = 0.08f;

        private Entity _entity = Entity.Null;
        private int _speedHash, _movingHash, _idleHash, _runHash, _currentHash;
        private int _hitLightHash, _hitHeavyHash;
        private float _movingTime, _runTime;
        private float _hitLockTimer;
        private float _previousKnockbackTimer;
        private float _previousMotionLockTimer;
        private int   _previousHitVersion;
        private bool _wasKnockedBack;

        public Entity Entity => _entity;
        public bool IsBound => _entity != Entity.Null;
        public float RequiredMotionLockTimer => _hitLockTimer;
        private Transform Root => visualRoot != null ? visualRoot : transform;

        private void Awake() => CacheHashes();
        private void OnValidate() => CacheHashes();

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
                previous = Root.position;

            previous.y += selectedPathYOffset;

            DynamicBuffer<NavAgentWaypoint> waypoints = em.GetBuffer<NavAgentWaypoint>(_entity, true);
            Gizmos.color = selectedPathColor;
            for (int i = 0; i < waypoints.Length; i++)
            {
                Vector3 waypoint = waypoints[i].Position;
                waypoint.y += selectedPathYOffset;
                Gizmos.DrawLine(previous, waypoint);
                previous = waypoint;
            }

            Gizmos.color = selectedWaypointColor;
            for (int i = 0; i < waypoints.Length; i++)
            {
                Vector3 waypoint = waypoints[i].Position;
                waypoint.y += selectedPathYOffset;
                Gizmos.DrawSphere(waypoint, selectedWaypointRadius);
            }
        }

        public void Bind(Entity entity, in LocalTransform initialTransform)
        {
            _entity = entity;
            _currentHash = 0;
            _movingTime = 0f;
            _runTime = 0f;
            _hitLockTimer = 0f;
            _previousKnockbackTimer = 0f;
            _previousMotionLockTimer = 0f;
            _previousHitVersion = 0;
            _wasKnockedBack = false;
            Root.SetPositionAndRotation(initialTransform.Position, initialTransform.Rotation);
        }

        public void Unbind()
        {
            _entity = Entity.Null;
            _wasKnockedBack = false;
            _hitLockTimer = 0f;
            _previousKnockbackTimer = 0f;
            _previousMotionLockTimer = 0f;
            _previousHitVersion = 0;
            if (CanUseAnimator())
                ApplyAnim(0f, false);
        }

        public void Tick(in LocalTransform ecsTransform, in NavAgentMotion motion, in NavAgentKnockback knockback, float deltaTime)
        {
            Transform root = Root;
            if (syncPosition)
                root.position = Damp(root.position, ecsTransform.Position, PositionSharpness, deltaTime);
            if (syncRotation)
                root.rotation = Quaternion.Slerp(root.rotation, ecsTransform.Rotation, DampFactor(RotationSharpness, deltaTime));

            bool isKnockedBack = knockback.Timer > 0f;
            bool isLocked      = knockback.MotionLockTimer > 0f;

            bool newKnockback  = isKnockedBack && (!_wasKnockedBack || knockback.Timer > _previousKnockbackTimer + 0.0001f);
            bool newMotionLock = knockback.MotionLockTimer > _previousMotionLockTimer + 0.0001f;
            bool newHitVersion = knockback.HitVersion != _previousHitVersion && knockback.HitVersion != 0;

            if (newKnockback || newMotionLock || newHitVersion)
            {
                bool heavy = knockback.InitialSpeed >= HitHeavyThreshold;
                int hash = heavy ? _hitHeavyHash : _hitLightHash;
                if (hash != 0) ForcePlay(hash, HitTransition);
                StartHitLock(hash, heavy ? HitHeavyStateName : HitLightStateName);
            }

            if (_hitLockTimer > 0f)
                _hitLockTimer = math.max(0f, _hitLockTimer - deltaTime);

            if (!isLocked && _hitLockTimer <= 0f)
                ApplyAnim(motion.CurrentSpeed, motion.IsMoving != 0);

            _wasKnockedBack = isKnockedBack;
            _previousKnockbackTimer = knockback.Timer;
            _previousMotionLockTimer = knockback.MotionLockTimer;
            _previousHitVersion = knockback.HitVersion;
        }

        public void TickIdle() => ApplyAnim(0f, false);

        private void ApplyAnim(float speed, bool moving)
        {
            if (!CanUseAnimator()) return;
            if (animationMode == AnimMode.CrossFade)
            {
                ApplyCrossFade(moving, Time.deltaTime);
                return;
            }
            if (_speedHash != 0) animator.SetFloat(_speedHash, speed);
            if (_movingHash != 0) animator.SetBool(_movingHash, moving);
        }

        private void ApplyCrossFade(bool moving, float deltaTime)
        {
            if (_idleHash == 0 || _runHash == 0) return;
            if (moving)
            {
                _movingTime += deltaTime;
                _runTime += deltaTime;
                if (_currentHash != _runHash && _movingTime >= RunEnterDelay)
                {
                    _runTime = 0f;
                    Play(_runHash, StartRunTransition);
                }
                return;
            }
            _movingTime = 0f;
            if (_currentHash == _runHash)
            {
                _runTime += deltaTime;
                if (_runTime < MinRunDuration) return;
            }
            Play(_idleHash, StopRunTransition);
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

        private bool CanUseAnimator()
            => animator != null && animator.gameObject.activeInHierarchy;

        private void StartHitLock(int stateHash, string stateName)
        {
            float duration = GetHitAnimationDuration(stateHash, stateName);
            float lockDuration = duration * math.clamp(HitLockClipRatio, 0.1f, 1f) + math.max(0f, HitPostAnimationHold);
            if (HitMaxLockDuration > 0f)
                lockDuration = math.min(lockDuration, HitMaxLockDuration);

            _hitLockTimer = math.max(_hitLockTimer, lockDuration);
            _movingTime = 0f;
            _runTime = 0f;
        }

        private float GetHitAnimationDuration(int stateHash, string stateName)
        {
            if (animator == null)
                return math.max(0f, HitFallbackLockDuration);

            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
            if (MatchesState(current, stateHash))
                return math.max(0f, current.length);

            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            if (MatchesState(next, stateHash))
                return math.max(0f, next.length);

            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            if (controller != null && !string.IsNullOrWhiteSpace(stateName))
            {
                AnimationClip[] clips = controller.animationClips;
                for (int i = 0; i < clips.Length; i++)
                {
                    AnimationClip clip = clips[i];
                    if (clip != null && clip.name == stateName)
                        return math.max(0f, clip.length);
                }
            }

            return math.max(0f, HitFallbackLockDuration);
        }

        private static bool MatchesState(AnimatorStateInfo stateInfo, int stateHash)
            => stateHash != 0 && (stateInfo.shortNameHash == stateHash || stateInfo.fullPathHash == stateHash);

        private void CacheHashes()
        {
            _idleHash     = string.IsNullOrWhiteSpace(IdleStateName)     ? 0 : Animator.StringToHash(IdleStateName);
            _runHash      = string.IsNullOrWhiteSpace(RunStateName)      ? 0 : Animator.StringToHash(RunStateName);
            _hitLightHash = string.IsNullOrWhiteSpace(HitLightStateName) ? 0 : Animator.StringToHash(HitLightStateName);
            _hitHeavyHash = string.IsNullOrWhiteSpace(HitHeavyStateName) ? 0 : Animator.StringToHash(HitHeavyStateName);
            _speedHash    = ParamHash(SpeedParameter,  AnimatorControllerParameterType.Float);
            _movingHash   = ParamHash(MovingParameter, AnimatorControllerParameterType.Bool);
        }

        private int ParamHash(string name, AnimatorControllerParameterType type)
        {
            if (animator == null || string.IsNullOrWhiteSpace(name)) return 0;
            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter p = parameters[i];
                if (p.name == name && p.type == type) return p.nameHash;
            }
            return 0;
        }

        private static Vector3 Damp(Vector3 current, Vector3 target, float sharpness, float deltaTime)
            => Vector3.Lerp(current, target, DampFactor(sharpness, deltaTime));

        private static float DampFactor(float sharpness, float deltaTime)
        {
            if (sharpness <= 0f) return 1f;
            return 1f - math.exp(-sharpness * deltaTime);
        }

        private string IdleStateName => visualProfile != null ? visualProfile.IdleStateName : idleStateName;
        private string RunStateName => visualProfile != null ? visualProfile.RunStateName : runStateName;
        private float StartRunTransition => visualProfile != null ? visualProfile.StartRunTransition : startRunTransition;
        private float StopRunTransition => visualProfile != null ? visualProfile.StopRunTransition : stopRunTransition;
        private float RunEnterDelay => visualProfile != null ? visualProfile.RunEnterDelay : runEnterDelay;
        private float MinRunDuration => visualProfile != null ? visualProfile.MinRunDuration : minRunDuration;
        private string SpeedParameter => visualProfile != null ? visualProfile.SpeedParameter : speedParameter;
        private string MovingParameter => visualProfile != null ? visualProfile.MovingParameter : movingParameter;
        private float PositionSharpness => visualProfile != null ? visualProfile.PositionSharpness : positionSharpness;
        private float RotationSharpness => visualProfile != null ? visualProfile.RotationSharpness : rotationSharpness;

        private string HitLightStateName => hitReactionProfile != null ? hitReactionProfile.LightStateName : hitLightStateName;
        private string HitHeavyStateName => hitReactionProfile != null ? hitReactionProfile.HeavyStateName : hitHeavyStateName;
        private float HitHeavyThreshold => hitReactionProfile != null ? hitReactionProfile.HeavyThreshold : hitHeavyThreshold;
        private float HitTransition => hitReactionProfile != null ? hitReactionProfile.Transition : hitTransition;
        private float HitLockClipRatio => hitReactionProfile != null ? hitReactionProfile.LockClipRatio : hitLockClipRatio;
        private float HitPostAnimationHold => hitReactionProfile != null ? hitReactionProfile.PostAnimationHold : hitPostAnimationHold;
        private float HitMaxLockDuration => hitReactionProfile != null ? hitReactionProfile.MaxLockDuration : hitMaxLockDuration;
        private float HitFallbackLockDuration => hitReactionProfile != null ? hitReactionProfile.FallbackLockDuration : hitFallbackLockDuration;
    }
}
