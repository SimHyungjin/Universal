using UnityEngine;

// Character_ActionHandler의 점프 분리분. 점프 호(상승 커브·낙하·착지 감지)의 실제 물리는
// Character_VerticalMotion이 소유하고, 여기서는 상태머신·입력(더블점프·착지 리커버리)만 다룬다.
public partial class Character_ActionHandler
{
    private void EnterJump()
    {
        _jumpCount = 1;
        StartJumpArc();
    }

    private void StartJumpArc()
    {
        _jumpArcVersion++;
        _jumpBufferTimer = 0f;
        _coyoteTimer = 0f;
        _jumpEndPlayed = false;
        SetState(Character_ActionState.Jump);
        _stateTimer = 0f;
        _vertical.StartJumpArc(JumpHeight, JumpRiseTime, JumpAscentDuration);
        // JumpStart/JumpIdle 공중 포즈는 Character_Animator가 IsAirborne으로 단독 재생한다.
        _vfx?.PlayJumpAfterimages();
    }

    private void TickJump(float deltaTime)
    {
        Vector3 worldInput = GetMoveWorld();

        if (_commandSource != null && _commandSource.ConsumeDash() && _dashCooldownTimer <= 0f)
        {
            EnterDash(worldInput, deltaTime);
            return;
        }

        if (_jumpEndPlayed)
        {
            TickJumpLanding(worldInput, deltaTime);
            return;
        }

        if (TryStartAirJump())
            return;

        _moveController.TickPlanarLocomotion(worldInput * JumpAirMoveScale, deltaTime);
        if (_vertical.TickJumpArc(deltaTime))
            CompleteJumpLanding();
    }

    private void TickJumpLanding(Vector3 worldInput, float deltaTime)
    {
        _stateTimer -= deltaTime;
        _moveController.TickLocomotion(worldInput * JumpLandingMoveScale, deltaTime);

        if (_stateTimer > 0f) return;

        _jumpEndPlayed = false;
        EnterNormal();
    }

    private bool TryStartAirJump()
    {
        if (_jumpBufferTimer <= 0f || _jumpCount >= MaxJumpCount)
            return false;

        _jumpCount++;
        StartJumpArc();
        return true;
    }

    public void InterruptJumpArcForAttack()
    {
        if (_state != Character_ActionState.Jump || _jumpEndPlayed)
            return;

        _vertical.CutJumpArcToFall();
    }

    private void CompleteJumpLanding()
    {
        // Jump_End 애니는 Character_Animator가 착지 에지에서 단독 재생한다(게임플레이 착지락과 분리).
        // 여기서는 게임플레이 락(JumpLandingRecoveryTime)만 관리 — 0이면 즉시 Normal로 복귀해 반응성 유지.
        _jumpEndPlayed = true;
        _stateTimer = Mathf.Max(0f, JumpLandingRecoveryTime);

        if (_stateTimer <= 0f)
            EnterNormal();
    }

    private void CompleteBlockedActionJumpLandingIfGrounded()
    {
        if (_state != Character_ActionState.Jump || !_moveController.IsGrounded)
            return;

        _jumpEndPlayed = false;
        // 다른 Normal 복귀 경로와 동일하게 EnterNormal로 통일한다(_jumpCount=0 + 로코모션 해제 +
        // hitReaction 스케일 리셋). 직접 _state=Normal만 하던 기존 경로는 ReleaseLocomotion을 건너뛰어
        // 막힌 액션 착지 후 로코모션이 억제된 채 남는 잔류 버그가 있었다.
        EnterNormal();
    }
}
