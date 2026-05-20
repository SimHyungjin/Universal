using UnityEngine;
using UnityEngine.Serialization;

public enum PlayerActionState
{
    Normal = 0,
    Jump = 1,
    Dash = 2,
    Hitstun = 3,
    Knockback = 4,
    Down = 5,
    Wakeup = 6
}

[RequireComponent(typeof(Player_Movecontroller))]
[RequireComponent(typeof(Player_Animator))]
[RequireComponent(typeof(Player_Attackcontroller))]
[DisallowMultipleComponent]
public class Player_ActionHandler : LoopMonoBehaviour, IDamageable
{
    [SerializeField] private SO_PlayerMoveData moveData;
    [SerializeField] private SO_PlayerAnimationData animationData;
    [SerializeField] private SO_PlayerActionData actionData;
    [SerializeField] private SO_AttackData[] attackDatas;
    [SerializeField] private float comboWindow = 0.35f;

    public PlayerActionState State => _state;
    public bool IsInvincible => _invincibleTimer > 0f;
    public bool CanAttack => _state == PlayerActionState.Normal;
    public bool LocksLocomotion => _state != PlayerActionState.Normal;

    private Player_Movecontroller _moveController;
    private Player_Attackcontroller _attackController;
    private Player_Animator _animator;
    private Player_Vfx _vfx;
    private PlayerActionState _state;
    private Vector3 _forcedDirection;
    private float _forcedSpeed;
    private float _stateTimer;
    private float _invincibleTimer;
    private float _dashCooldownTimer;
    private float _coyoteTimer;
    private float _jumpBufferTimer;
    private bool _jumpLeftGround;
    private bool _jumpLiftStarted;
    private bool _jumpIdlePlayed;
    private bool _jumpEndPlayed;

    private void Awake()
    {
        _moveController = GetComponent<Player_Movecontroller>();
        _attackController = GetComponent<Player_Attackcontroller>();
        _animator = GetComponent<Player_Animator>();
        _vfx = GetComponent<Player_Vfx>();
        _moveController.SetMoveData(moveData);
        _animator.SetAnimationData(animationData);
        _attackController.SetAttackData(attackDatas, comboWindow);
    }

    protected override void OnGameUpdate(float gdt)
    {
        base.OnGameUpdate(gdt);

        TickSharedTimers(gdt);
        BufferJumpInput();

        bool combatActive = Main.Input.IsActive<InputActions_Combat>();

        Vector3 worldInput = Main.Input.IsActive<InputActions_Move>()
            ? Player_Movecontroller.GetCameraRelativeInput(InputProvider.Move.Direction)
            : Vector3.zero;

        _attackController?.UpdateLookDirection(worldInput);

        bool attackPressed = combatActive && InputProvider.ConsumeAttack();
        if (attackPressed && _attackController != null && (_state == PlayerActionState.Normal || _attackController.IsInCombo))
            _attackController.RequestAttack();

        if (_attackController != null && _attackController.IsInCombo)
        {
            _moveController.MoveVertical(gdt);
            return;
        }

        switch (_state)
        {
            case PlayerActionState.Dash:
                TickForcedMove(gdt, PlayerActionState.Normal);
                break;
            case PlayerActionState.Jump:
                TickJump(gdt);
                break;
            case PlayerActionState.Hitstun:
                TickHitstun(gdt);
                break;
            case PlayerActionState.Knockback:
                TickKnockback(gdt);
                break;
            case PlayerActionState.Down:
                TickDown(gdt);
                break;
            case PlayerActionState.Wakeup:
                TickWakeup(gdt);
                break;
            default:
                TickNormal(gdt, worldInput);
                break;
        }
    }

    public void ReceiveHit(Vector3 hitSource, AttackKnockbackData knockback, float hitstunDuration)
    {
        if (IsInvincible) return;

        _attackController?.CancelAttack();
        _vfx?.StopDash();

        Vector3 direction = transform.position - hitSource;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = -transform.forward;
        direction.Normalize();

        _forcedDirection = direction;
        _forcedSpeed = Mathf.Max(0f, knockback.force);
        _stateTimer = Mathf.Max(hitstunDuration, knockback.duration);
        _moveController.StopPlanar();

        if (knockback.force >= DownKnockbackThreshold)
        {
            EnterDown();
            return;
        }

        _state = knockback.duration > 0f ? PlayerActionState.Knockback : PlayerActionState.Hitstun;
        PlayAction(HitStateName);
    }

    public void ReceiveHit(Vector3 hitSource, SO_AttackData attack)
    {
        if (attack == null) return;
        ReceiveHit(hitSource, attack.Knockback, attack.Hitstun.duration);
    }

    public void TakeDamage(float amount, Vector3 hitSource, float knockbackForce = 0f)
    {
        ReceiveHit(hitSource, new AttackKnockbackData
        {
            force = knockbackForce,
            duration = knockbackForce > 0f ? HitstunDuration : 0f,
            friction = KnockbackFriction
        }, HitstunDuration);
    }

    private void TickNormal(float deltaTime, Vector3 worldInput)
    {
        if (InputProvider.ConsumeDash() && _dashCooldownTimer <= 0f)
        {
            EnterDash(worldInput, deltaTime);
            return;
        }

        if (_jumpBufferTimer > 0f && _coyoteTimer > 0f)
        {
            EnterJump();
            return;
        }

        _moveController.TickLocomotion(worldInput, deltaTime);
    }

    private void EnterJump()
    {
        _jumpBufferTimer = 0f;
        _coyoteTimer = 0f;
        _jumpLeftGround = false;
        _jumpLiftStarted = false;
        _jumpIdlePlayed = false;
        _jumpEndPlayed = false;
        _state = PlayerActionState.Jump;
        _stateTimer = Mathf.Max(0f, JumpAnticipationTime);
        PlayAction(JumpStartStateName);

        if (_stateTimer <= 0f)
            StartJumpLift();
    }

    private void TickJump(float deltaTime)
    {
        Vector3 worldInput = Main.Input.IsActive<InputActions_Move>()
            ? Player_Movecontroller.GetCameraRelativeInput(InputProvider.Move.Direction)
            : Vector3.zero;

        if (!_jumpLiftStarted)
        {
            TickJumpAnticipation(worldInput, deltaTime);
            return;
        }

        if (_jumpEndPlayed)
        {
            TickJumpLanding(worldInput, deltaTime);
            return;
        }

        _moveController.TickLocomotion(worldInput, deltaTime);

        if (!_moveController.IsGrounded)
            _jumpLeftGround = true;

        if (ShouldPlayJumpIdle())
        {
            _jumpIdlePlayed = true;
            PlayAction(JumpIdleStateName);
        }

        if (!_jumpLeftGround || !_moveController.IsGrounded || _moveController.VerticalVelocity > 0f)
            return;

        if (!_jumpEndPlayed)
        {
            _jumpEndPlayed = true;
            _stateTimer = Mathf.Max(0f, JumpLandingRecoveryTime);
            PlayAction(JumpEndStateName);
            return;
        }
    }

    private void TickJumpAnticipation(Vector3 worldInput, float deltaTime)
    {
        _stateTimer -= deltaTime;
        _moveController.TickLocomotion(worldInput * JumpAnticipationMoveScale, deltaTime);

        if (_stateTimer > 0f) return;

        StartJumpLift();
    }

    private void TickJumpLanding(Vector3 worldInput, float deltaTime)
    {
        _stateTimer -= deltaTime;
        _moveController.TickLocomotion(worldInput * JumpLandingMoveScale, deltaTime);

        if (_stateTimer > 0f) return;

        _jumpIdlePlayed = false;
        _jumpEndPlayed = false;
        _state = PlayerActionState.Normal;
        _animator.ReleaseLocomotion();
    }

    private void StartJumpLift()
    {
        if (_jumpLiftStarted) return;

        _jumpLiftStarted = true;
        _moveController.Jump(JumpHeight);
    }

    private bool ShouldPlayJumpIdle()
    {
        if (_jumpIdlePlayed || string.IsNullOrWhiteSpace(JumpIdleStateName))
            return false;

        if (string.IsNullOrWhiteSpace(JumpStartStateName))
            return _jumpLeftGround && _moveController.VerticalVelocity <= 0f;

        return _animator.HasCurrentStateReachedEnd(JumpStartStateName);
    }

    private void EnterDash(Vector3 input, float deltaTime)
    {
        Vector3 direction = ResolveActionDirection(input);
        float duration = Mathf.Max(0.01f, DashDuration);
        _state = PlayerActionState.Dash;
        _stateTimer = duration;
        _forcedDirection = direction;
        _forcedSpeed = DashDistance / duration;
        _invincibleTimer = Mathf.Max(_invincibleTimer, DashInvincibleDuration);
        _dashCooldownTimer = DashCooldown;
        _moveController.StopPlanar();
        _moveController.RotateTowards(direction, deltaTime);
        _vfx?.PlayDashStart(direction);
        PlayAction(DashStateName);
    }

    private void TickForcedMove(float deltaTime, PlayerActionState nextState)
    {
        _stateTimer -= deltaTime;
        _moveController.MoveVelocity(_forcedDirection * _forcedSpeed, deltaTime, true);

        if (_stateTimer > 0f) return;

        bool endedDash = _state == PlayerActionState.Dash;
        _state = nextState;
        if (endedDash)
            _vfx?.PlayDashEnd(_forcedDirection);

        if (_state == PlayerActionState.Normal)
            _animator.ReleaseLocomotion();
    }

    private void TickHitstun(float deltaTime)
    {
        _stateTimer -= deltaTime;
        _moveController.MoveVertical(deltaTime);

        if (_stateTimer > 0f) return;

        _state = PlayerActionState.Normal;
        _animator.ReleaseLocomotion();
    }

    private void TickKnockback(float deltaTime)
    {
        _stateTimer -= deltaTime;
        _forcedSpeed = Mathf.MoveTowards(_forcedSpeed, 0f, KnockbackFriction * deltaTime);
        _moveController.MoveVelocity(_forcedDirection * _forcedSpeed, deltaTime, true);

        if (_stateTimer > 0f) return;

        _state = PlayerActionState.Normal;
        _animator.ReleaseLocomotion();
    }

    private void EnterDown()
    {
        _state = PlayerActionState.Down;
        _stateTimer = DownDuration;
        _moveController.StopPlanar();
        PlayAction(DownStateName);
    }

    private void TickDown(float deltaTime)
    {
        _stateTimer -= deltaTime;
        _forcedSpeed = Mathf.MoveTowards(_forcedSpeed, 0f, KnockbackFriction * deltaTime);
        _moveController.MoveVelocity(_forcedDirection * _forcedSpeed, deltaTime, true);

        if (_stateTimer > 0f) return;

        _state = PlayerActionState.Wakeup;
        _stateTimer = WakeupDuration;
        _invincibleTimer = Mathf.Max(_invincibleTimer, WakeupInvincibleDuration);
        PlayAction(WakeupStateName);
    }

    private void TickWakeup(float deltaTime)
    {
        _stateTimer -= deltaTime;
        _moveController.MoveVertical(deltaTime);

        if (_stateTimer > 0f) return;

        _state = PlayerActionState.Normal;
        _animator.ReleaseLocomotion();
    }

    private void TickSharedTimers(float deltaTime)
    {
        _invincibleTimer = Mathf.Max(0f, _invincibleTimer - deltaTime);
        _dashCooldownTimer = Mathf.Max(0f, _dashCooldownTimer - deltaTime);
        _jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - deltaTime);

        _coyoteTimer = _moveController.IsGrounded
            ? CoyoteTime
            : Mathf.Max(0f, _coyoteTimer - deltaTime);
    }

    private void BufferJumpInput()
    {
        if (InputProvider.ConsumeJump())
            _jumpBufferTimer = JumpBufferTime;
    }

    private Vector3 ResolveActionDirection(Vector3 input)
    {
        if (input.sqrMagnitude > 0.0001f)
            return input.normalized;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }

    private void PlayAction(string stateName)
    {
        _animator.PlayAction(stateName, ActionTransition);
    }

    private float JumpHeight => moveData != null ? moveData.JumpHeight : 1.6f;
    private float JumpAnticipationTime => moveData != null ? moveData.JumpAnticipationTime : 0.05f;
    private float JumpAnticipationMoveScale => moveData != null ? moveData.JumpAnticipationMoveScale : 0f;
    private float JumpLandingRecoveryTime => moveData != null ? moveData.JumpLandingRecoveryTime : 0.12f;
    private float JumpLandingMoveScale => moveData != null ? moveData.JumpLandingMoveScale : 0f;
    private float CoyoteTime => moveData != null ? moveData.CoyoteTime : 0.08f;
    private float JumpBufferTime => moveData != null ? moveData.JumpBufferTime : 0.1f;
    private float DashDistance => moveData != null ? moveData.DashDistance : 4f;
    private float DashDuration => moveData != null ? moveData.DashDuration : 0.16f;
    private float DashCooldown => moveData != null ? moveData.DashCooldown : 0.35f;
    private float DashInvincibleDuration => moveData != null ? moveData.DashInvincibleDuration : 0.12f;
    private float HitstunDuration => actionData != null ? actionData.HitstunDuration : 0.35f;
    private float KnockbackFriction => actionData != null ? actionData.KnockbackFriction : 14f;
    private float DownKnockbackThreshold => actionData != null ? actionData.DownKnockbackThreshold : 12f;
    private float DownDuration => actionData != null ? actionData.DownDuration : 1f;
    private float WakeupDuration => actionData != null ? actionData.WakeupDuration : 0.65f;
    private float WakeupInvincibleDuration => actionData != null ? actionData.WakeupInvincibleDuration : 0.45f;
    private string JumpStartStateName => animationData != null ? animationData.JumpStartStateName : "";
    private string JumpIdleStateName => animationData != null ? animationData.JumpIdleStateName : "";
    private string JumpEndStateName => animationData != null ? animationData.JumpEndStateName : "";
    private string DashStateName => animationData != null ? animationData.DashStateName : "";
    private string HitStateName => animationData != null ? animationData.HitStateName : "";
    private string DownStateName => animationData != null ? animationData.DownStateName : "";
    private string WakeupStateName => animationData != null ? animationData.WakeupStateName : "";
    private float ActionTransition => animationData != null ? animationData.ActionTransition : 0.05f;
}
