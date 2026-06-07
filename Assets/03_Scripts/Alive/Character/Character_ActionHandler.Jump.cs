using UnityEngine;

// Character_ActionHandler의 점프(아케이드 아크) 분리분. 상태머신·필드는 본체와 공유한다(partial).
// 상승: EvaluateJumpAscentHeight 커브로 _jumpGroundY 기준 y를 직접 구동. 정점 이후: 중력 낙하 → 착지.
public partial class Character_ActionHandler
{
    private void EnterJump()
    {
        _jumpCount = 1;
        StartJumpArc();
    }

    private void StartJumpArc()
    {
        _jumpBufferTimer = 0f;
        _coyoteTimer = 0f;
        _jumpArcElapsed = 0f;
        _jumpGroundY = transform.position.y;
        _jumpFallingStarted = false;
        _jumpIdlePlayed = false;
        _jumpEndPlayed = false;
        _state = Character_ActionState.Jump;
        _stateTimer = 0f;
        _moveController.SetVerticalVelocity(0f);
        PlayAction(JumpStartStateName);
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
        TickArcadeJumpArc(deltaTime);

        if (ShouldPlayJumpIdle())
        {
            _jumpIdlePlayed = true;
            PlayAction(JumpIdleStateName);
        }
    }

    private void TickJumpLanding(Vector3 worldInput, float deltaTime)
    {
        _stateTimer -= deltaTime;
        _moveController.TickLocomotion(worldInput * JumpLandingMoveScale, deltaTime);

        if (_stateTimer > 0f) return;

        _jumpIdlePlayed = false;
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

        _jumpArcElapsed = JumpAscentDuration;
        _jumpFallingStarted = true;
        _jumpIdlePlayed = false;
    }

    private void TickArcadeJumpArc(float deltaTime)
    {
        _jumpArcElapsed += deltaTime;

        if (_jumpArcElapsed <= JumpAscentDuration)
        {
            float desiredY = _jumpGroundY + EvaluateJumpAscentHeight(_jumpArcElapsed);
            _moveController.MoveDisplacement(new Vector3(0f, desiredY - transform.position.y, 0f));
            return;
        }

        if (!_jumpFallingStarted)
        {
            _jumpFallingStarted = true;
            _moveController.SetVerticalVelocity(0f);
        }

        _moveController.MoveVertical(deltaTime);
        if (!_moveController.IsGrounded)
            return;

        CompleteJumpLanding();
    }

    private void CompleteJumpLanding()
    {
        _jumpEndPlayed = true;
        _stateTimer = Mathf.Max(0f, JumpLandingRecoveryTime);
        if (!string.IsNullOrWhiteSpace(JumpEndStateName))
            PlayAction(JumpEndStateName);

        if (_stateTimer <= 0f)
            EnterNormal();
    }

    private void CompleteBlockedActionJumpLandingIfGrounded()
    {
        if (_state != Character_ActionState.Jump || !_moveController.IsGrounded)
            return;

        _state = Character_ActionState.Normal;
        _jumpCount = 0;
        _jumpIdlePlayed = false;
        _jumpEndPlayed = false;
        _jumpFallingStarted = false;
    }

    private float EvaluateJumpAscentHeight(float elapsed)
    {
        float riseTime = JumpRiseTime;
        float height = JumpHeight;

        if (elapsed <= riseTime)
        {
            float t = Mathf.Clamp01(elapsed / riseTime);
            return height * EaseOutCubic(t);
        }

        return height;
    }

    private bool ShouldPlayJumpIdle()
    {
        if (_jumpIdlePlayed || string.IsNullOrWhiteSpace(JumpIdleStateName))
            return false;

        if (string.IsNullOrWhiteSpace(JumpStartStateName))
            return _jumpArcElapsed >= JumpRiseTime;

        return _jumpArcElapsed >= JumpRiseTime || _animator.HasCurrentStateReachedEnd(JumpStartStateName);
    }

    private static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }
}
