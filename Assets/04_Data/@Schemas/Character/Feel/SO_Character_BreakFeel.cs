using UnityEngine;

[CreateAssetMenu(fileName = "SO_Character_BreakFeel", menuName = "Game/Character/Feel/Break Feel")]
public sealed class SO_Character_BreakFeel : ScriptableObject
{
    [Header("Vulnerable")]
    [SerializeField, Min(0f)] private float brokenDuration = 1.5f;
    [SerializeField, Min(1f)] private float brokenDamageTakenMultiplier = 1.35f;
    [SerializeField, Range(0f, 1f)] private float recoveryRatioOnBrokenEnd = 1f;
    [SerializeField, Min(0f)] private float recoveryDelay = 1.5f;
    [SerializeField, Min(0f)] private float recoveryPerSecond = 60f;

    [Header("Break Feedback")]
    [SerializeField, Min(0f)] private float shakeAmplitude = 0.38f;
    [SerializeField, Min(0f)] private float shakeDuration = 0.32f;
    [SerializeField, Min(0f)] private float shakeFrequency = 36f;
    [SerializeField, Min(0f)] private float hitstopDuration = 0.2f;
    [SerializeField, Range(0f, 1f)] private float hitstopTimeScale = 0.01f;
    [SerializeField] private SkillCutInData cameraCue = new()
    {
        enabled = false,
        duration = 0.1f,
        fovOverride = 38f,
        distanceOverride = 4.4f,
        heightDelta = -0.35f,
        yawVelocity = 18f
    };
    [SerializeField] private RenderingLayerMask brokenOutlineRenderingLayerMask = 8u;

    public float BrokenDuration => brokenDuration;
    public float BrokenDamageTakenMultiplier => brokenDamageTakenMultiplier;
    public float RecoveryRatioOnBrokenEnd => recoveryRatioOnBrokenEnd;
    public float RecoveryDelay => recoveryDelay;
    public float RecoveryPerSecond => recoveryPerSecond;
    public float ShakeAmplitude => shakeAmplitude;
    public float ShakeDuration => shakeDuration;
    public float ShakeFrequency => shakeFrequency;
    public float HitstopDuration => hitstopDuration;
    public float HitstopTimeScale => hitstopTimeScale;
    public SkillCutInData CameraCue => cameraCue;
    public uint BrokenOutlineRenderingLayerMask => brokenOutlineRenderingLayerMask;
}
