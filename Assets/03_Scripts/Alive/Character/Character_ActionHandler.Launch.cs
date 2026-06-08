using MapNav.Ecs;
using UnityEngine;

// Character_ActionHandler의 launch(공중 부양) 분리분. launch의 수직 적분·groundY·궤적은
// Character_VerticalMotion이 소유하고, 여기서는 수평 넉백·pendingDown·착지 결과(상태 전이)만 다룬다.
public partial class Character_ActionHandler
{
    // 공중 부양. _forcedDirection/_forcedSpeed는 호출 직전 ReceiveHit에서 넉백 값으로 세팅돼 있다.
    // 수직 y 구동·체공·groundY는 _vertical이 잡몹(NavLaunchSystem)과 동일한 LaunchPhysics로 통일한다.
    private void EnterOrRefreshLaunch(AttackLaunchData launch, AttackDownData down)
    {
        bool wasLaunched = _state == Character_ActionState.Launched;
        SetState(Character_ActionState.Launched);
        _vertical.StartLaunch(launch.height, launch.suspendDuration, wasLaunched);
        _launchPendingDown = down.enabled;
        _launchDownDuration = Mathf.Max(0f, down.duration);
        _moveController.StopPlanar();
        PlayHitReaction(HitReactionKind.Launch);
    }

    private void TickLaunched(float deltaTime)
    {
        // 수평 성분: 공중에선 friction 없이 일정 속도(거리 조절은 launch.knockback.force).
        Vector3 displacement = _forcedDirection * (_forcedSpeed * deltaTime);

        // 수직 적분·착지 판정은 _vertical이 담당. yDelta를 받아 수평과 합쳐 단일 MoveDisplacement.
        bool landed = _vertical.TickLaunchVertical(displacement, deltaTime, out float yDelta);
        displacement.y = yDelta;
        _moveController.MoveDisplacement(displacement);

        if (!landed)
            return;

        if (_launchPendingDown)
            EnterDown(_launchDownDuration);
        else
            EnterNormal();
    }
}
