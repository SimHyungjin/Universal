using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Character_VerticalMotion))]
[DisallowMultipleComponent]
public class Character_MoveController : LoopMonoBehaviour
{
    private SO_Character_Stats stats;
    private SO_Character_LocomotionFeel locomotionFeel;

    private CharacterController _cc;
    private Character_VerticalMotion _vertical;
    private Vector3 _planarVelocity;
    private Vector3 _lungeDirection;
    private float _lungeDistance;
    private float _lungeDuration;
    private float _lungeElapsed;
    private float _lungeTimer;
    private AnimationCurve _lungeSpeedCurve;

    public bool IsGrounded => _cc != null && _cc.isGrounded;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
    }

    // 수직 운동 위임 대상. RequireComponent로 보장되지만, Awake 순서/런타임 인스턴스 안전을 위해 지연 해석.
    private Character_VerticalMotion Vertical
        => _vertical != null
            ? _vertical
            : (_vertical = GetComponent<Character_VerticalMotion>() ?? gameObject.AddComponent<Character_VerticalMotion>());

    public void SetMovementData(SO_Character_Stats stats, SO_Character_LocomotionFeel feel, SO_WorldPhysics physics)
    {
        if (stats != null) this.stats = stats;
        if (feel != null) this.locomotionFeel = feel;
        Vertical.SetWorldPhysics(physics);
    }

    public void StartLunge(Vector3 direction, AttackLungeData lunge)
    {
        // distance 음수 = 후진(백스텝). 부호는 TickLunge에서 그대로 흐른다. 0만 거른다.
        // (이동 취소는 별도 StopLunge() 경로가 담당)
        if (Mathf.Approximately(lunge.distance, 0f) || lunge.duration <= 0f) return;

        _lungeDirection = new Vector3(direction.x, 0f, direction.z).normalized;
        _lungeDistance = lunge.distance;
        _lungeDuration = lunge.duration;
        _lungeElapsed = 0f;
        _lungeTimer = lunge.duration;
        _lungeSpeedCurve = lunge.speedCurve;
    }

    public void StopLunge()
    {
        _lungeTimer = 0f;
        _lungeElapsed = 0f;
        _lungeDistance = 0f;
        _lungeDuration = 0f;
        _lungeSpeedCurve = null;
    }

    public void TickLocomotion(Vector3 input, float deltaTime)
    {
        Vector3 targetVelocity = Vector3.ClampMagnitude(input, 1f) * MaxSpeed;
        float speedChange = input.sqrMagnitude > 0.0001f ? Acceleration : Deceleration;
        _planarVelocity = Vector3.MoveTowards(_planarVelocity, targetVelocity, speedChange * deltaTime);

        Vertical.TickGravity(_cc.isGrounded, deltaTime);

        Vector3 velocity = _planarVelocity;
        velocity.y = Vertical.VerticalVelocity;

        Rotate(input, deltaTime);
        _cc.Move(velocity * deltaTime);
    }

    public void TickPlanarLocomotion(Vector3 input, float deltaTime)
    {
        Vector3 targetVelocity = Vector3.ClampMagnitude(input, 1f) * MaxSpeed;
        float speedChange = input.sqrMagnitude > 0.0001f ? Acceleration : Deceleration;
        _planarVelocity = Vector3.MoveTowards(_planarVelocity, targetVelocity, speedChange * deltaTime);

        Rotate(input, deltaTime);
        _cc.Move(_planarVelocity * deltaTime);
    }

    public void MoveVertical(float deltaTime)
    {
        Vertical.TickGravity(_cc.isGrounded, deltaTime);
        _cc.Move(new Vector3(0f, Vertical.VerticalVelocity * deltaTime, 0f));
    }

    public void SetVerticalVelocity(float velocity)
    {
        Vertical.SetVerticalVelocity(velocity);
    }

    public void MoveDisplacement(Vector3 displacement)
    {
        _cc.Move(displacement);
    }

    public void MoveVelocity(Vector3 velocity, float deltaTime, bool applyGravity)
    {
        if (applyGravity)
        {
            Vertical.TickGravity(_cc.isGrounded, deltaTime);
            velocity.y = Vertical.VerticalVelocity;
        }

        _cc.Move(velocity * deltaTime);
    }

    public void Jump(float height)
    {
        Vertical.ApplyJumpImpulse(height);
    }

    public void StopPlanar()
    {
        _planarVelocity = Vector3.zero;
    }

    public void RotateTowards(Vector3 direction, float deltaTime)
    {
        Rotate(direction, deltaTime);
    }

    protected override void OnGameUpdate(float gdt)
    {
        base.OnGameUpdate(gdt);
        if (_lungeTimer <= 0f) return;

        float previousElapsed = _lungeElapsed;
        _lungeElapsed = Mathf.Min(_lungeDuration, _lungeElapsed + gdt);
        _lungeTimer = Mathf.Max(0f, _lungeDuration - _lungeElapsed);

        float previousT = _lungeDuration > 0f ? previousElapsed / _lungeDuration : 1f;
        float currentT = _lungeDuration > 0f ? _lungeElapsed / _lungeDuration : 1f;
        float previousDistanceT = EvaluateLungeDistanceT(previousT);
        float currentDistanceT = EvaluateLungeDistanceT(currentT);
        float distanceDelta = Mathf.Max(0f, currentDistanceT - previousDistanceT) * _lungeDistance;

        _cc.Move(_lungeDirection * distanceDelta);
    }

    private float EvaluateLungeDistanceT(float t)
    {
        if (!HasUsableLungeCurve())
            return t;

        return Mathf.Clamp01(_lungeSpeedCurve.Evaluate(t));
    }

    private bool HasUsableLungeCurve()
    {
        if (_lungeSpeedCurve == null || _lungeSpeedCurve.length < 2)
            return false;

        float start = _lungeSpeedCurve.Evaluate(0f);
        float end = _lungeSpeedCurve.Evaluate(1f);
        return end > start + 0.0001f;
    }

    private void Rotate(Vector3 input, float deltaTime)
    {
        if (input.sqrMagnitude <= 0.0001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(input);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, RotationInterpolationSpeed * deltaTime);
    }

    public static Vector3 GetCameraRelativeInput(Vector2 input)
    {
        Camera cam = Camera.main;
        if (cam == null) return new Vector3(input.x, 0f, input.y);

        float yaw = cam.transform.eulerAngles.y;
        Quaternion yawOnly = Quaternion.Euler(0f, yaw, 0f);
        Vector3 forward = yawOnly * Vector3.forward;
        Vector3 right   = yawOnly * Vector3.right;

        return Vector3.ClampMagnitude((right * input.x) + (forward * input.y), 1f);
    }

    private float MaxSpeed => stats != null ? stats.MoveSpeed : 5f;
    private float Acceleration => locomotionFeel != null ? locomotionFeel.Acceleration : 80f;
    private float Deceleration => locomotionFeel != null ? locomotionFeel.Deceleration : 100f;
    private float RotationInterpolationSpeed => locomotionFeel != null ? locomotionFeel.RotationInterpolationSpeed : 30f;
}
