using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class Player_Movecontroller : LoopMonoBehaviour
{
    private SO_PlayerMoveData moveData;

    private float moveSpeed = 5f;
    private float rotationSpeed = 12f;
    private float gravity = -20f;

    private CharacterController _cc;
    private Vector3 _planarVelocity;
    private float _verticalVelocity;
    private Vector3 _lungeDirection;
    private float _lungeSpeed;
    private float _lungeTimer;

    public bool IsGrounded => _cc != null && _cc.isGrounded;
    public float VerticalVelocity => _verticalVelocity;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
    }

    public void SetMoveData(SO_PlayerMoveData data)
    {
        if (data != null)
            moveData = data;
    }

    public void StartLunge(Vector3 direction, float distance, float duration)
    {
        if (distance <= 0f || duration <= 0f) return;

        _lungeDirection = new Vector3(direction.x, 0f, direction.z).normalized;
        _lungeSpeed = distance / duration;
        _lungeTimer = duration;
    }

    public void TickLocomotion(Vector3 input, float deltaTime)
    {
        Vector3 targetVelocity = Vector3.ClampMagnitude(input, 1f) * MaxSpeed;
        float speedChange = input.sqrMagnitude > 0.0001f ? Acceleration : Deceleration;
        _planarVelocity = Vector3.MoveTowards(_planarVelocity, targetVelocity, speedChange * deltaTime);

        TickGravity(deltaTime);

        Vector3 velocity = _planarVelocity;
        velocity.y = _verticalVelocity;

        Rotate(input, deltaTime);
        _cc.Move(velocity * deltaTime);
    }

    public void TickGravity(float deltaTime)
    {
        if (_cc.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = GroundedStickVelocity;

        _verticalVelocity += Gravity * deltaTime;
    }

    public void MoveVertical(float deltaTime)
    {
        TickGravity(deltaTime);
        _cc.Move(new Vector3(0f, _verticalVelocity * deltaTime, 0f));
    }

    public void MoveDisplacement(Vector3 displacement)
    {
        _cc.Move(displacement);
    }

    public void MoveVelocity(Vector3 velocity, float deltaTime, bool applyGravity)
    {
        if (applyGravity)
        {
            TickGravity(deltaTime);
            velocity.y = _verticalVelocity;
        }

        _cc.Move(velocity * deltaTime);
    }

    public void Jump(float height)
    {
        _verticalVelocity = Mathf.Sqrt(Mathf.Max(0f, height) * -2f * Gravity);
    }

    public void StopPlanar()
    {
        _planarVelocity = Vector3.zero;
    }

    public void RotateTowards(Vector3 direction, float deltaTime)
    {
        Rotate(direction, deltaTime);
    }

    protected override void OnUpdate(float dt)
    {
        base.OnUpdate(dt);
        if (_lungeTimer <= 0f) return;

        _lungeTimer -= dt;
        _cc.Move(_lungeDirection * _lungeSpeed * dt);
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

    private float MaxSpeed => moveData != null ? moveData.MaxSpeed : moveSpeed;
    private float Acceleration => moveData != null ? moveData.Acceleration : 1000f;
    private float Deceleration => moveData != null ? moveData.Deceleration : 1000f;
    private float RotationInterpolationSpeed => moveData != null ? moveData.RotationInterpolationSpeed : rotationSpeed;
    private float Gravity => moveData != null ? moveData.Gravity : gravity;
    private float GroundedStickVelocity => moveData != null ? moveData.GroundedStickVelocity : -1f;
}
