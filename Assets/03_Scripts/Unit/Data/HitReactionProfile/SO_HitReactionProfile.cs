using UnityEngine;

namespace MapNav.Ecs
{
    [CreateAssetMenu(fileName = "SO_HitReactionProfile", menuName = "Game/Combat/Hit Reaction Profile")]
    public sealed class SO_HitReactionProfile : ScriptableObject
    {
        [Header("Animation States")]
        [SerializeField] private string lightStateName = "Hit_0";
        [SerializeField] private string heavyStateName = "Hit_1";
        [SerializeField] private float heavyThreshold = 30f;
        [SerializeField] private float transition = 0.05f;

        [Header("Motion Lock")]
        [SerializeField, Range(0.1f, 1f)] private float lockClipRatio = 1f;
        [SerializeField] private float postAnimationHold = 0.2f;
        [SerializeField] private float maxLockDuration = 2f;
        [SerializeField] private float fallbackLockDuration = 0.05f;

        public string LightStateName => lightStateName;
        public string HeavyStateName => heavyStateName;
        public float HeavyThreshold => heavyThreshold;
        public float Transition => transition;
        public float LockClipRatio => lockClipRatio;
        public float PostAnimationHold => postAnimationHold;
        public float MaxLockDuration => maxLockDuration;
        public float FallbackLockDuration => fallbackLockDuration;
    }
}
