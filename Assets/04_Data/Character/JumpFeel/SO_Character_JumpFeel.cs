using UnityEngine;

[CreateAssetMenu(fileName = "SO_Character_JumpFeel", menuName = "Game/Character/Jump Feel")]
public sealed class SO_Character_JumpFeel : ScriptableObject
{
    [Header("Anticipation")]
    [SerializeField] private float jumpAnticipationTime = 0.2f;
    [SerializeField, Range(0f, 1f)] private float jumpAnticipationMoveScale = 0.2f;

    [Header("Landing")]
    [SerializeField] private float jumpLandingRecoveryTime = 0.15f;
    [SerializeField, Range(0f, 1f)] private float jumpLandingMoveScale = 0.1f;

    [Header("Height")]
    [Tooltip("최종 jumpHeight = moveSpeed × jumpHeightPerSpeed (baseline: 5×0.4=2)")]
    [SerializeField] private float jumpHeightPerSpeed = 0.4f;

    public float JumpAnticipationTime => jumpAnticipationTime;
    public float JumpAnticipationMoveScale => jumpAnticipationMoveScale;
    public float JumpLandingRecoveryTime => jumpLandingRecoveryTime;
    public float JumpLandingMoveScale => jumpLandingMoveScale;
    public float JumpHeightPerSpeed => jumpHeightPerSpeed;
}
