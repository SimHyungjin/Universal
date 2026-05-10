using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MapNavEcsVisualShell : MonoBehaviour
{
    private enum AnimationMode
    {
        StateCrossFade = 0,
        Parameters = 1
    }

    [SerializeField] private Transform visualRoot;
    [SerializeField] private Animator animator;
    [SerializeField] private AnimationMode animationMode = AnimationMode.StateCrossFade;
    [SerializeField] private string idleStateName = "Idle";
    [SerializeField] private string runStateName = "Run";
    [SerializeField] private float startRunTransition = 0.05f;
    [SerializeField] private float stopRunTransition = 0.18f;
    [SerializeField] private float runEnterDelay = 0.06f;
    [SerializeField] private float minRunDuration = 0.18f;
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string movingParameter = "Moving";
    [SerializeField] private string directionXParameter = "";
    [SerializeField] private string directionZParameter = "";
    [SerializeField] private bool syncPosition = true;
    [SerializeField] private bool syncRotation = true;
    [SerializeField] private float positionSharpness = 40f;
    [SerializeField] private float rotationSharpness = 40f;

    private Entity _entity = Entity.Null;
    private int _speedHash;
    private int _movingHash;
    private int _directionXHash;
    private int _directionZHash;
    private int _idleStateHash;
    private int _runStateHash;
    private int _currentStateHash;
    private float _movingTime;
    private float _runTime;

    public Entity Entity => _entity;
    public bool IsBound => _entity != Entity.Null;

    private Transform Root => visualRoot != null ? visualRoot : transform;

    private void Awake()
    {
        CacheAnimatorHashes();
    }

    private void OnValidate()
    {
        CacheAnimatorHashes();
    }

    public void Bind(Entity entity, in LocalTransform initialTransform)
    {
        _entity = entity;
        _currentStateHash = 0;
        _movingTime = 0f;
        _runTime = 0f;
        Root.SetPositionAndRotation(initialTransform.Position, initialTransform.Rotation);
    }

    public void Unbind()
    {
        _entity = Entity.Null;
        ApplyAnimator(0f, false, float3.zero);
    }

    public void Tick(in LocalTransform ecsTransform, in MapNavEcsMotionState motion, float deltaTime)
    {
        Transform root = Root;

        if (syncPosition)
            root.position = Damp(root.position, ecsTransform.Position, positionSharpness, deltaTime);

        if (syncRotation)
            root.rotation = Quaternion.Slerp(root.rotation, ecsTransform.Rotation, DampFactor(rotationSharpness, deltaTime));

        ApplyAnimator(motion.CurrentSpeed, motion.IsMoving != 0, motion.Velocity);
    }

    public void TickIdle()
    {
        ApplyAnimator(0f, false, float3.zero);
    }

    private void ApplyAnimator(float speed, bool moving, float3 velocity)
    {
        if (animator == null)
            return;

        if (animationMode == AnimationMode.StateCrossFade)
        {
            ApplyAnimatorState(moving, Time.deltaTime);
            return;
        }

        if (_speedHash != 0)
            animator.SetFloat(_speedHash, speed);
        if (_movingHash != 0)
            animator.SetBool(_movingHash, moving);
        if (_directionXHash != 0)
            animator.SetFloat(_directionXHash, velocity.x);
        if (_directionZHash != 0)
            animator.SetFloat(_directionZHash, velocity.z);
    }

    private void CacheAnimatorHashes()
    {
        _idleStateHash = string.IsNullOrWhiteSpace(idleStateName) ? 0 : Animator.StringToHash(idleStateName);
        _runStateHash = string.IsNullOrWhiteSpace(runStateName) ? 0 : Animator.StringToHash(runStateName);
        _speedHash = GetAnimatorParameterHash(speedParameter, AnimatorControllerParameterType.Float);
        _movingHash = GetAnimatorParameterHash(movingParameter, AnimatorControllerParameterType.Bool);
        _directionXHash = GetAnimatorParameterHash(directionXParameter, AnimatorControllerParameterType.Float);
        _directionZHash = GetAnimatorParameterHash(directionZParameter, AnimatorControllerParameterType.Float);
    }

    private void ApplyAnimatorState(bool moving, float deltaTime)
    {
        if (_idleStateHash == 0 || _runStateHash == 0)
            return;

        if (moving)
        {
            _movingTime += deltaTime;
            _runTime += deltaTime;

            if (_currentStateHash != _runStateHash && _movingTime >= runEnterDelay)
            {
                _runTime = 0f;
                Play(_runStateHash, startRunTransition);
            }

            return;
        }

        _movingTime = 0f;

        if (_currentStateHash == _runStateHash)
        {
            _runTime += deltaTime;
            if (_runTime < minRunDuration)
                return;
        }

        Play(_idleStateHash, stopRunTransition);
    }

    private void Play(int stateHash, float transitionDuration)
    {
        if (_currentStateHash == stateHash)
            return;

        _currentStateHash = stateHash;
        animator.CrossFade(stateHash, transitionDuration);
    }

    private int GetAnimatorParameterHash(string parameterName, AnimatorControllerParameterType expectedType)
    {
        if (animator == null || string.IsNullOrWhiteSpace(parameterName))
            return 0;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.name == parameterName && parameter.type == expectedType)
                return parameter.nameHash;
        }

        return 0;
    }

    private static Vector3 Damp(Vector3 current, Vector3 target, float sharpness, float deltaTime)
    {
        return Vector3.Lerp(current, target, DampFactor(sharpness, deltaTime));
    }

    private static float DampFactor(float sharpness, float deltaTime)
    {
        if (sharpness <= 0f)
            return 1f;

        return 1f - math.exp(-sharpness * deltaTime);
    }
}
