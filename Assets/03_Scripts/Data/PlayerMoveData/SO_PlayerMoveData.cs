using UnityEngine;

[CreateAssetMenu(fileName = "SO_PlayerMoveData", menuName = "Game/Player/Move Data")]
public sealed class SO_PlayerMoveData : ScriptableObject
{
    [Header("Locomotion")]
    [SerializeField] private float maxSpeed = 5f;
    [SerializeField] private float acceleration = 80f;
    [SerializeField] private float deceleration = 100f;
    [SerializeField] private float rotationInterpolationSpeed = 12f;

    [Header("Vertical")]
    [SerializeField] private float gravity = -30f;
    [SerializeField] private float groundedStickVelocity = -1f;
    [SerializeField] private float jumpHeight = 1.6f;
    [SerializeField] private float jumpAnticipationTime = 0.05f;
    [SerializeField, Range(0f, 1f)] private float jumpAnticipationMoveScale = 0f;
    [SerializeField] private float jumpLandingRecoveryTime = 0.12f;
    [SerializeField, Range(0f, 1f)] private float jumpLandingMoveScale = 0f;
    [SerializeField] private float coyoteTime = 0.08f;
    [SerializeField] private float jumpBufferTime = 0.1f;

    [Header("Dash")]
    [SerializeField] private float dashDistance = 4f;
    [SerializeField] private float dashDuration = 0.16f;
    [SerializeField] private float dashCooldown = 0.35f;
    [SerializeField] private float dashInvincibleDuration = 0.12f;

    public float MaxSpeed => maxSpeed;
    public float Acceleration => acceleration;
    public float Deceleration => deceleration;
    public float RotationInterpolationSpeed => rotationInterpolationSpeed;
    public float Gravity => gravity;
    public float GroundedStickVelocity => groundedStickVelocity;
    public float JumpHeight => jumpHeight;
    public float JumpAnticipationTime => jumpAnticipationTime;
    public float JumpAnticipationMoveScale => jumpAnticipationMoveScale;
    public float JumpLandingRecoveryTime => jumpLandingRecoveryTime;
    public float JumpLandingMoveScale => jumpLandingMoveScale;
    public float CoyoteTime => coyoteTime;
    public float JumpBufferTime => jumpBufferTime;
    public float DashDistance => dashDistance;
    public float DashDuration => dashDuration;
    public float DashCooldown => dashCooldown;
    public float DashInvincibleDuration => dashInvincibleDuration;
}
