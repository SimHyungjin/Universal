using UnityEngine;

[CreateAssetMenu(fileName = "SO_InputBuffering", menuName = "Game/Common/Input Buffering")]
public sealed class SO_InputBuffering : ScriptableObject
{
    [SerializeField] private float coyoteTime = 0.08f;
    [SerializeField] private float jumpBufferTime = 0.1f;

    public float CoyoteTime => coyoteTime;
    public float JumpBufferTime => jumpBufferTime;
}
