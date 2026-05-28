// 이동/점프/대시 파생값 계산식의 단일 진실. 속도 비례 모델이 바뀌면 여기만 수정한다.
public static class LocomotionFormula
{
    // jumpHeight = moveSpeed × jumpHeightPerSpeed
    public static float ScaleJumpHeight(float moveSpeed, float jumpHeightPerSpeed)
        => moveSpeed * jumpHeightPerSpeed;

    // dashSpeed = moveSpeed × dashSpeedMultiplier
    public static float ScaleDashSpeed(float moveSpeed, float dashSpeedMultiplier)
        => moveSpeed * dashSpeedMultiplier;

    // dashDistance = dashSpeed × dashDuration = moveSpeed × dashSpeedMultiplier × dashDuration
    public static float ScaleDashDistance(float moveSpeed, float dashSpeedMultiplier, float dashDuration)
        => moveSpeed * dashSpeedMultiplier * dashDuration;
}
