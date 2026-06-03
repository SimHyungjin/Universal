using Unity.Mathematics;

// 공중 부양(launch)의 수직 물리 단일 진실. 잡몹(ECS NavLaunchSystem, Burst)과
// 캐릭터(Character_ActionHandler, Mono)가 같은 중력·같은 궤적으로 뜨도록 공유한다.
// UnityEngine 의존 없음 — Burst Job 컨텍스트에서도 안전하게 호출 가능.
// 모델: 포물선 커브가 아니라 "초기 상승속도 + 중력 적분"(점프와 동일). CharacterController.Jump와 일치.
public static class LaunchPhysics
{
    // 공유 중력. SO_WorldPhysics 기본값과 일치시켜 두 스택의 궤적을 통일한다.
    // ECS는 이 상수를, 캐릭터는 WorldPhysics.Gravity(없으면 이 값)를 넘긴다.
    public const float Gravity = -15f;

    // 목표 높이 height에 도달하는 초기 상승 속도. v = sqrt(2·|g|·h).
    public static float InitialVelocity(float height, float gravity)
        => math.sqrt(2f * math.abs(gravity) * math.max(0f, height));

    // Repeated launch hits should not restart the same ascent every hit tick.
    // Let a new launch re-pop targets once they are falling, but preserve an
    // existing upward arc while the target is already airborne and rising.
    public static float RefreshVelocityForLaunchHit(float currentVerticalVelocity, float initialVelocity, bool isAirborne)
        => isAirborne && currentVerticalVelocity > 0f ? currentVerticalVelocity : initialVelocity;

    // 수직 위치를 한 스텝 적분한다. 상승이 끝난 정점(vy<=0)에서 suspendTimer가 남아 있으면
    // 그 시간만큼 낙하를 보류(체공)한 뒤 중력 적분을 재개한다.
    // ceiling(= 지면 + launch height)을 넘지 않게 clamp한다 — repeat 재타격으로 상승속도가 매번
    // 재부여돼도 천장까지 계단식으로 올라가지 않고 launch height에서 hover하게 만든다.
    public static void Integrate(ref float y, ref float verticalVelocity, float gravity, float dt, ref float suspendTimer, float ceiling)
    {
        if (suspendTimer > 0f && verticalVelocity <= 0f)
        {
            suspendTimer = math.max(0f, suspendTimer - dt);
            verticalVelocity = 0f;
            return;
        }

        verticalVelocity += gravity * dt;
        y += verticalVelocity * dt;

        if (y > ceiling)
        {
            y = ceiling;
            if (verticalVelocity > 0f)
            {
                verticalVelocity = 0f;
                // ceiling 클램프로 velocity=0이 됐을 때 suspendTimer 조건이 즉시 트리거되지 않도록 리셋.
                // suspend는 자연 정점(중력으로 velocity가 음수가 된 첫 프레임)에서만 작동해야 한다.
                suspendTimer = 0f;
            }
        }
    }
}
