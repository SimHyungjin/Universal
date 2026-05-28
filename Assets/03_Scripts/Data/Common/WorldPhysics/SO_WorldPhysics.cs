using UnityEngine;

[CreateAssetMenu(fileName = "SO_WorldPhysics", menuName = "Game/Common/World Physics")]
public sealed class SO_WorldPhysics : ScriptableObject
{
    [SerializeField] private float gravity = -15f;
    [SerializeField] private float groundedStickVelocity = -1f;

    public float Gravity => gravity;
    public float GroundedStickVelocity => groundedStickVelocity;
}
