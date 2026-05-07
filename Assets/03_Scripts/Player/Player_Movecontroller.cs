using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class Player_Movecontroller : LoopMonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 12f;
    [SerializeField] private float gravity = -20f;

    private CharacterController _cc;
    private float _verticalVelocity;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
    }

    protected override void OnGameUpdate(float gdt)
    {
        base.OnGameUpdate(gdt);
        if (!Main.Input.IsActive<InputActions_Move>()) return;

        Vector2 input = InputProvider.Move.Direction;
        Move(GetCameraRelativeInput(input), gdt);
    }

    private void Move(Vector3 input, float deltaTime)
    {
        Vector3 planarVelocity = Vector3.ClampMagnitude(input, 1f) * moveSpeed;

        if (_cc.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -1f;

        _verticalVelocity += gravity * deltaTime;

        Vector3 velocity = planarVelocity;
        velocity.y = _verticalVelocity;

        if (input.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(input);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * deltaTime
            );
        }

        _cc.Move(velocity * deltaTime);
    }

    private static Vector3 GetCameraRelativeInput(Vector2 input)
    {
        Camera cam = Camera.main;
        if (cam == null) return new Vector3(input.x, 0f, input.y);

        Vector3 forward = cam.transform.forward;
        Vector3 right = cam.transform.right;

        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        return Vector3.ClampMagnitude((right * input.x) + (forward * input.y), 1f);
    }
}
