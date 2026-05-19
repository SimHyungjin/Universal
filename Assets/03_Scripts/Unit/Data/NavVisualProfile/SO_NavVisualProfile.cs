using UnityEngine;

namespace MapNav.Ecs
{
    [CreateAssetMenu(fileName = "SO_NavVisualProfile", menuName = "Game/Nav/Visual Profile")]
    public sealed class SO_NavVisualProfile : ScriptableObject
    {
        [Header("Locomotion States")]
        [SerializeField] private string idleStateName = "Idle";
        [SerializeField] private string runStateName = "Run";
        [SerializeField] private float startRunTransition = 0.05f;
        [SerializeField] private float stopRunTransition = 0.18f;
        [SerializeField] private float runEnterDelay = 0.06f;
        [SerializeField] private float minRunDuration = 0.18f;

        [Header("Animator Parameters")]
        [SerializeField] private string speedParameter = "Speed";
        [SerializeField] private string movingParameter = "Moving";

        [Header("Transform Sync")]
        [SerializeField] private float positionSharpness = 40f;
        [SerializeField] private float rotationSharpness = 40f;

        public string IdleStateName => idleStateName;
        public string RunStateName => runStateName;
        public float StartRunTransition => startRunTransition;
        public float StopRunTransition => stopRunTransition;
        public float RunEnterDelay => runEnterDelay;
        public float MinRunDuration => minRunDuration;
        public string SpeedParameter => speedParameter;
        public string MovingParameter => movingParameter;
        public float PositionSharpness => positionSharpness;
        public float RotationSharpness => rotationSharpness;
    }
}
