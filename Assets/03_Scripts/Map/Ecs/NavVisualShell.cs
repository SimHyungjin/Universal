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
        [SerializeField] private string idleStateName = "Idle";
        [SerializeField] private string runStateName = "Run";
        [SerializeField] private float startRunTransition = 0.05f;
        [SerializeField] private float stopRunTransition = 0.18f;
        [SerializeField] private float runEnterDelay = 0.06f;
        [SerializeField] private float minRunDuration = 0.18f;
        [SerializeField] private string speedParameter = "Speed";
        [SerializeField] private string movingParameter = "Moving";
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
        private int _speedHash, _movingHash, _idleHash, _runHash, _currentHash;
        private float _movingTime, _runTime;

        public Entity Entity => _entity;
        public bool IsBound => _entity != Entity.Null;
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
            Root.SetPositionAndRotation(initialTransform.Position, initialTransform.Rotation);
        }

        public void Unbind()
        {
            _entity = Entity.Null;
            ApplyAnim(0f, false);
        }

        public void Tick(in LocalTransform ecsTransform, in NavAgentMotion motion, float deltaTime)
        {
            Transform root = Root;
            if (syncPosition)
                root.position = Damp(root.position, ecsTransform.Position, positionSharpness, deltaTime);
            if (syncRotation)
                root.rotation = Quaternion.Slerp(root.rotation, ecsTransform.Rotation, DampFactor(rotationSharpness, deltaTime));
            ApplyAnim(motion.CurrentSpeed, motion.IsMoving != 0);
        }

        public void TickIdle() => ApplyAnim(0f, false);

        private void ApplyAnim(float speed, bool moving)
        {
            if (animator == null) return;
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
                if (_currentHash != _runHash && _movingTime >= runEnterDelay)
                {
                    _runTime = 0f;
                    Play(_runHash, startRunTransition);
                }
                return;
            }
            _movingTime = 0f;
            if (_currentHash == _runHash)
            {
                _runTime += deltaTime;
                if (_runTime < minRunDuration) return;
            }
            Play(_idleHash, stopRunTransition);
        }

        private void Play(int stateHash, float transitionDuration)
        {
            if (_currentHash == stateHash) return;
            _currentHash = stateHash;
            animator.CrossFade(stateHash, transitionDuration);
        }

        private void CacheHashes()
        {
            _idleHash = string.IsNullOrWhiteSpace(idleStateName) ? 0 : Animator.StringToHash(idleStateName);
            _runHash = string.IsNullOrWhiteSpace(runStateName) ? 0 : Animator.StringToHash(runStateName);
            _speedHash = ParamHash(speedParameter, AnimatorControllerParameterType.Float);
            _movingHash = ParamHash(movingParameter, AnimatorControllerParameterType.Bool);
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
    }
}
