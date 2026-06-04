using UnityEngine;

[CreateAssetMenu(fileName = "SO_Character_JumpFeel", menuName = "Game/Character/Jump Feel")]
public sealed class SO_Character_JumpFeel : ScriptableObject
{
    [Header("Anticipation")]
    [SerializeField] private float jumpAnticipationTime = 0f;
    [SerializeField, Range(0f, 1f)] private float jumpAnticipationMoveScale = 1f;

    [Header("Landing")]
    [SerializeField] private float jumpLandingRecoveryTime = 0f;
    [SerializeField, Range(0f, 1f)] private float jumpLandingMoveScale = 1f;

    [Header("Height")]
    [Tooltip("Final jumpHeight = moveSpeed * jumpHeightPerSpeed (baseline: 5 * 0.4 = 2).")]
    [SerializeField] private float jumpHeightPerSpeed = 0.4f;

    [Header("Jumps")]
    [SerializeField, Min(1)] private int maxJumpCount = 2;

    [Header("Arcade Arc")]
    [SerializeField, Min(0.01f)] private float jumpRiseTime = 0.1f;
    [SerializeField, Min(0f)] private float jumpApexHoldTime = 0.02f;
    [SerializeField, Range(0f, 2f)] private float jumpAirMoveScale = 1f;

    public float JumpAnticipationTime => jumpAnticipationTime;
    public float JumpAnticipationMoveScale => jumpAnticipationMoveScale;
    public float JumpLandingRecoveryTime => jumpLandingRecoveryTime;
    public float JumpLandingMoveScale => jumpLandingMoveScale;
    public float JumpHeightPerSpeed => jumpHeightPerSpeed;
    public int MaxJumpCount => maxJumpCount;
    public float JumpRiseTime => jumpRiseTime;
    public float JumpApexHoldTime => jumpApexHoldTime;
    public float JumpAirMoveScale => jumpAirMoveScale;
}
