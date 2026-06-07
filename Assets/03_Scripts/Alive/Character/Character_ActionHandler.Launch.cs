using MapNav.Ecs;
using UnityEngine;

// Character_ActionHandler의 launch(공중 부양) 분리분. 상태머신·필드는 본체와 공유한다(partial).
// 잡몹(NavLaunchSystem)과 동일한 LaunchPhysics(초기속도+중력)로 y를 구동해 궤적·체공을 통일한다.
public partial class Character_ActionHandler
{
    // 공중 부양. _forcedDirection/_forcedSpeed/_forcedFriction은 호출 직전 ReceiveHit에서 넉백 값으로 세팅돼 있다.
    // 잡몹(NavLaunchSystem)과 동일한 LaunchPhysics(초기속도+중력)로 y를 구동한다. repeat 재타격 시
    // 지면 기준(GroundY)은 유지하고 수직속도만 재부여해 체공을 연장한다(잡몹 HitboxProcessor와 동일 규칙).
    private void EnterOrRefreshLaunch(AttackLaunchData launch, AttackDownData down)
    {
        bool wasLaunched = _state == Character_ActionState.Launched;
        bool startsNewArc = !wasLaunched || _launchVerticalVelocity <= 0f;
        if (!wasLaunched)
            _launchGroundY = ResolveLaunchGroundY(transform.position);

        if (startsNewArc)
            _launchElapsed = 0f;

        _state = Character_ActionState.Launched;
        _launchHeight = Mathf.Max(0f, launch.height);
        // launch 중력은 잡몹(NavLaunchSystem)과 반드시 같은 값(LaunchPhysics.Gravity)을 써야 체공시간이
        // 일치한다. 일반 점프/낙하는 worldPhysics.Gravity를 쓰지만 launch는 잡몹과 통일한다.
        float initialVelocity = LaunchPhysics.InitialVelocity(launch.height, LaunchPhysics.Gravity);
        _launchVerticalVelocity = LaunchPhysics.RefreshVelocityForLaunchHit(_launchVerticalVelocity, initialVelocity, wasLaunched);
        _launchSuspendTimer = Mathf.Max(0f, launch.suspendDuration);
        _launchMaxDuration = ResolveLaunchMaxDuration(_launchHeight, _launchSuspendTimer);
        _launchPendingDown = down.enabled;
        _launchDownDuration = Mathf.Max(0f, down.duration);
        _moveController.StopPlanar();
        PlayHitReaction(HitReactionKind.Launch);
    }

    private void TickLaunched(float deltaTime)
    {
        _launchElapsed += deltaTime;
        // 포물선 수평 성분: 공중에서는 friction 없이 일정 속도 유지.
        // 착지 후 EnterDown/EnterNormal으로 이어지므로 거리 조절은 launch.knockback.force로 한다.

        float y = transform.position.y;
        float ceiling = _launchGroundY + _launchHeight;
        LaunchPhysics.Integrate(ref y, ref _launchVerticalVelocity, LaunchPhysics.Gravity, deltaTime, ref _launchSuspendTimer, ceiling);

        // 낙하해서 시작 지면 이하로 내려오면 착지. down이 예약돼 있으면 다운으로 이어진다.
        Vector3 displacement = _forcedDirection * (_forcedSpeed * deltaTime);
        Vector3 nextPlanarPosition = transform.position + new Vector3(displacement.x, 0f, displacement.z);
        float landingGroundY = ResolveLaunchGroundY(nextPlanarPosition);

        bool landed = _launchVerticalVelocity <= 0f && y <= landingGroundY;
        if (_launchMaxDuration > 0f && _launchElapsed >= _launchMaxDuration)
            landed = true;
        if (landed)
            y = landingGroundY;

        displacement.y = y - transform.position.y;
        _moveController.MoveDisplacement(displacement);

        if (!landed)
            return;

        if (_launchPendingDown)
            EnterDown(_launchDownDuration);
        else
            EnterNormal();
    }

    private float ResolveLaunchGroundY(Vector3 samplePosition)
    {
        if (TryResolveLaunchGroundY(samplePosition, out float groundY))
            return groundY;

        return _launchGroundY != 0f ? _launchGroundY : transform.position.y;
    }

    private bool TryResolveLaunchGroundY(Vector3 samplePosition, out float groundY)
    {
        float radius = _characterController != null
            ? Mathf.Max(0.05f, _characterController.radius * 0.85f)
            : 0.25f;
        Vector3 origin = samplePosition + Vector3.up * LaunchGroundProbeUp;
        int count = Physics.SphereCastNonAlloc(
            origin,
            radius,
            Vector3.down,
            LaunchGroundHits,
            LaunchGroundProbeUp + LaunchGroundProbeDown,
            ~0,
            QueryTriggerInteraction.Ignore);

        float bestDistance = float.MaxValue;
        groundY = 0f;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = LaunchGroundHits[i];
            Collider hitCollider = hit.collider;
            LaunchGroundHits[i] = default;
            if (hitCollider == null || ShouldIgnoreLaunchGroundHit(hitCollider))
                continue;

            if (hit.distance >= bestDistance)
                continue;

            bestDistance = hit.distance;
            groundY = hit.point.y;
            found = true;
        }

        return found;
    }

    private bool ShouldIgnoreLaunchGroundHit(Collider hitCollider)
    {
        if (hitCollider.transform == transform || hitCollider.transform.IsChildOf(transform))
            return true;

        if (hitCollider.GetComponentInParent<Character_ActionHandler>() != null)
            return true;

        if (hitCollider.GetComponentInParent<Unit_NavVisualShell>() != null)
            return true;

        return false;
    }

    private static float ResolveLaunchMaxDuration(float height, float suspendDuration)
    {
        float initialVelocity = LaunchPhysics.InitialVelocity(height, LaunchPhysics.Gravity);
        float airtime = initialVelocity > 0f
            ? initialVelocity * 2f / Mathf.Abs(LaunchPhysics.Gravity)
            : 0f;
        return Mathf.Clamp(airtime + Mathf.Max(0f, suspendDuration) + LaunchFailsafeExtraTime, 0.35f, LaunchFailsafeMaxDuration);
    }
}
