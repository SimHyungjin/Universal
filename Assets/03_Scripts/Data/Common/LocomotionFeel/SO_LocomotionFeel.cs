using UnityEngine;

[CreateAssetMenu(fileName = "SO_LocomotionFeel", menuName = "Game/Common/Locomotion Feel")]
public sealed class SO_LocomotionFeel : ScriptableObject
{
    [Header("Acceleration")]
    [SerializeField] private float acceleration = 80f;
    [SerializeField] private float deceleration = 100f;

    [Header("Rotation")]
    [SerializeField] private float rotationInterpolationSpeed = 30f;

    public float Acceleration => acceleration;
    public float Deceleration => deceleration;
    public float RotationInterpolationSpeed => rotationInterpolationSpeed;
}
