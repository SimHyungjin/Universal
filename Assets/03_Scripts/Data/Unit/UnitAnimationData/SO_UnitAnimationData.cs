using UnityEngine;

namespace MapNav.Ecs
{
    // 잡몹의 시각/애니메이션 단일 자산. 로코모션, 피격 리액션, ECS↔Mono 보간을 한곳에 정의한다.
    [CreateAssetMenu(fileName = "SO_UnitAnimationData", menuName = "Game/Unit/Animation Data")]
    public sealed class SO_UnitAnimationData : ScriptableObject
    {
        [Header("Locomotion States")]
        [SerializeField] private string idleStateName = "Idle";
        [SerializeField] private string runStateName = "Run";
        [SerializeField] private float startRunTransition = 0.05f;
        [SerializeField] private float stopRunTransition = 0.18f;
        [SerializeField] private float runEnterDelay = 0.06f;
        [SerializeField] private float minRunDuration = 0.18f;

        [Header("Hit Reaction States")]
        [SerializeField] private string lightStateName = "Hit_0";
        [SerializeField] private string heavyStateName = "Hit_1";
        [SerializeField] private string deathStateName = "Death";
        [SerializeField] private float hitTransition = 0.05f;

        [Header("Transform Sync")]
        [SerializeField] private float positionSharpness = 40f;
        [SerializeField] private float rotationSharpness = 40f;

        public string IdleStateName => idleStateName;
        public string RunStateName => runStateName;
        public float StartRunTransition => startRunTransition;
        public float StopRunTransition => stopRunTransition;
        public float RunEnterDelay => runEnterDelay;
        public float MinRunDuration => minRunDuration;

        public string LightStateName => lightStateName;
        public string HeavyStateName => heavyStateName;
        public string DeathStateName => deathStateName;
        public float HitTransition => hitTransition;

        public float PositionSharpness => positionSharpness;
        public float RotationSharpness => rotationSharpness;
    }
}
