using System;
using System.Threading;
using Cysharp.Threading.Tasks;
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
    Wakeup = 6,
    Dead = 7
}

[RequireComponent(typeof(Player_Movecontroller))]
[RequireComponent(typeof(Player_Animator))]
[RequireComponent(typeof(Player_Attackcontroller))]
[DisallowMultipleComponent]
public class Player_ActionHandler : LoopMonoBehaviour, IDamageable
{
    [SerializeField] private SO_PlayerData _playerData;

    private SO_PlayerStats statsData;
    private SO_PlayerAnimationData animationData;
    private SO_LocomotionFeel locomotionFeel;
    private SO_WorldPhysics worldPhysics;
    private SO_InputBuffering inputBuffering;
    private SO_JumpFeel jumpFeel;
    private SO_DashRule dashRule;
    private SO_ActionRecovery actionRecovery;

    public PlayerActionState State => _state;
    public bool IsInvincible => _invincibleTimer > 0f;
    public bool CanAttack => _state == PlayerActionState.Normal;
    public bool LocksLocomotion => _state != PlayerActionState.Normal;
    public float Health => _health;
    public float MaxHealth => statsData != null ? statsData.MaxHealth : 100f;
    public SO_SkillData GetSkillData(int slot)
    {
        if (_attackController != null) return _attackController.GetSkillData(slot);
        if (slot < 0 || _playerData == null || _playerData.Skills == null || slot >= _playerData.Skills.Length) return null;
        return _playerData.Skills[slot];
    }

    public float GetSkillCooldown(int slot)
        => _attackController != null ? _attackController.GetSkillCooldown(slot) : 0f;

    public float GetSkillCooldownDuration(int slot)
    {
        SO_SkillData skill = GetSkillData(slot);
        return skill != null ? skill.Cooldown : 0f;
    }

    // HUD/디버그 패널이 구독한다. 데미지/회복/사망/시작 시점에 (current, max)를 발사.
    public event System.Action<float, float> OnHealthChanged;

    private Player_Movecontroller _moveController;
    private Player_Attackcontroller _attackController;
    private Player_Animator _animator;
    private Player_Vfx _vfx;
    private PlayerActionState _state;
    private float _health;
    private Vector3 _forcedDirection;
    private float _forcedSpeed;
    private float _forcedFriction;
    private float _stateTimer;
    private float _invincibleTimer;
    private float _dashCooldownTimer;
    private float _hitReactionDurationScale = 1f;
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

        if (_playerData != null)
        {
            statsData = _playerData.StatsData;
            animationData = _playerData.AnimationData;
            locomotionFeel = _playerData.LocomotionFeel;
            worldPhysics = _playerData.WorldPhysics;
            inputBuffering = _playerData.InputBuffering;
            jumpFeel = _playerData.JumpFeel;
            dashRule = _playerData.DashRule;
            actionRecovery = _playerData.ActionRecovery;
        }

        _moveController.SetMovementData(statsData, locomotionFeel, worldPhysics);
        _animator.SetAnimationData(animationData);
        _attackController.SetPlayerStats(statsData);
        _attackController.SetBasicAttackCombo(_playerData != null ? _playerData.AttackCombo : null);
        _attackController.SetSkills(_playerData != null ? _playerData.Skills : null);
        _health = MaxHealth;
    }

    protected override void OnGameUpdate(float gdt)
    {
        base.OnGameUpdate(gdt);

        if (_state == PlayerActionState.Dead)
        {
            TickDead(gdt);
            return;
        }

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

        if (_attackController != null && _attackController.BlocksMovement)
        {
            if (_attackController.SuspendsAtApex)
                _moveController.MoveVerticalUntilApexThenSuspend(gdt);
            else
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

    public void ReceiveHit(
        Vector3 hitSource,
        AttackKnockbackData knockback,
        float damage,
        AttackHitstopData hitstop = default,
        AttackDownData down = default,
        float superArmorBreak = 0f)
    {
        if (IsInvincible || _state == PlayerActionState.Dead) return;

        ApplyDamage(damage);
        if (_state == PlayerActionState.Dead) return;
        TriggerHitstop(hitstop);

        if (_attackController != null && _attackController.IsSuperArmoredAgainst(superArmorBreak))
            return;

        float hitReactionDurationScale = ConsumeHitReactionDurationScale();
        float scaledReactionDuration = Mathf.Max(0f, down.duration) * hitReactionDurationScale;
        float scaledKnockbackDuration = Mathf.Max(0f, knockback.duration) * hitReactionDurationScale;
        float stateDuration = Mathf.Max(scaledReactionDuration, scaledKnockbackDuration);

        _attackController?.CancelAttack();
        _vfx?.StopDash();

        Vector3 direction = transform.position - hitSource;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = -transform.forward;
        direction.Normalize();

        _forcedDirection = direction;
        _forcedSpeed = Mathf.Max(0f, knockback.force);
        _forcedFriction = Mathf.Max(0f, knockback.friction);
        _stateTimer = stateDuration;
        _moveController.StopPlanar();

        if (down.enabled)
        {
            EnterDown(stateDuration);
            return;
        }

        _state = scaledKnockbackDuration > 0f ? PlayerActionState.Knockback : PlayerActionState.Hitstun;
        PlayAction(HitStateName);
    }

    public void ReceiveHit(Vector3 hitSource, SO_AttackData attack)
    {
        if (attack == null) return;
        ReceiveHit(
            hitSource,
            attack.Knockback,
            attack.Damage,
            attack.Hitstop,
            attack.Down,
            attack.SuperArmorBreak);
    }

    private void TriggerHitstop(AttackHitstopData hitstop)
    {
        if (hitstop.duration <= 0f || Main.Loop == null) return;
        DoHitstop(hitstop, destroyCancellationToken).Forget();
    }

    private static async UniTaskVoid DoHitstop(AttackHitstopData hitstop, CancellationToken token)
    {
        // 월드만 잠시 정지/감속, 플레이어는 그대로 — LoopManager.SetTimeScales(world, player) 시그니처를 따른다.
        Main.Loop.SetTimeScales(Mathf.Clamp01(hitstop.timeScale), 1f);
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(hitstop.duration), DelayType.UnscaledDeltaTime, cancellationToken: token);
        }
        catch (OperationCanceledException) { return; }
        if (Main.Loop != null)
            Main.Loop.SetTimeScales(1f, 1f);
    }

    public void TakeDamage(float amount, Vector3 hitSource, float knockbackForce = 0f)
    {
        ReceiveHit(hitSource, new AttackKnockbackData
        {
            force = knockbackForce,
            duration = knockbackForce > 0f ? FallbackReactionDuration : 0f,
            friction = FallbackKnockbackFriction
        }, amount, down: new AttackDownData { duration = FallbackReactionDuration });
    }

    private void ApplyDamage(float amount)
    {
        if (amount <= 0f) return;

        float reduced = statsData != null ? CombatFormula.ReduceIncomingDamage(statsData.Defense, amount) : amount;
        _health = Mathf.Max(0f, _health - reduced);
        OnHealthChanged?.Invoke(_health, MaxHealth);
        if (_health <= 0f)
            EnterDead();
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || _state == PlayerActionState.Dead) return;

        float max = MaxHealth;
        float next = Mathf.Min(max, _health + amount);
        if (Mathf.Approximately(next, _health)) return;

        _health = next;
        OnHealthChanged?.Invoke(_health, max);
    }

    private void EnterDead()
    {
        _state = PlayerActionState.Dead;
        _attackController?.CancelAttack();
        _vfx?.StopDash();
        _moveController.StopPlanar();
        PlayAction(DeathStateName);
    }

    private void TickDead(float deltaTime)
    {
        _moveController.MoveVertical(deltaTime);
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
        EnterNormal();
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
            EnterNormal();
    }

    private void TickHitstun(float deltaTime)
    {
        _stateTimer -= deltaTime;
        _moveController.MoveVertical(deltaTime);

        if (_stateTimer > 0f) return;

        EnterNormal();
    }

    private void TickKnockback(float deltaTime)
    {
        _stateTimer -= deltaTime;
        // 지수 감속. ECS NavKnockbackSystem과 동일한 곡선을 사용해 같은 friction 값이면 같은 거리만큼 미끄러진다.
        _forcedSpeed *= Mathf.Max(0f, 1f - _forcedFriction * deltaTime);
        _moveController.MoveVelocity(_forcedDirection * _forcedSpeed, deltaTime, true);

        if (_stateTimer > 0f) return;

        EnterNormal();
    }

    private void EnterDown(float duration)
    {
        _state = PlayerActionState.Down;
        _stateTimer = Mathf.Max(0f, duration);
        _moveController.StopPlanar();
        PlayAction(DownStateName);
    }

    private void TickDown(float deltaTime)
    {
        _stateTimer -= deltaTime;
        _forcedSpeed *= Mathf.Max(0f, 1f - _forcedFriction * deltaTime);
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

        EnterNormal();
    }

    private void EnterNormal()
    {
        _state = PlayerActionState.Normal;
        _hitReactionDurationScale = 1f;
        _animator.ReleaseLocomotion();
    }

    private float ConsumeHitReactionDurationScale()
    {
        if (!IsHitReactionState(_state))
        {
            _hitReactionDurationScale = 1f;
            return 1f;
        }

        _hitReactionDurationScale *= ChainedHitReactionDurationMultiplier;
        return _hitReactionDurationScale;
    }

    private static bool IsHitReactionState(PlayerActionState state)
        => state == PlayerActionState.Hitstun
           || state == PlayerActionState.Knockback
           || state == PlayerActionState.Down;

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

    private float JumpHeight => (jumpFeel != null && statsData != null) ? LocomotionFormula.ScaleJumpHeight(statsData.MoveSpeed, jumpFeel.JumpHeightPerSpeed) : 2f;
    private float JumpAnticipationTime => jumpFeel != null ? jumpFeel.JumpAnticipationTime : 0.2f;
    private float JumpAnticipationMoveScale => jumpFeel != null ? jumpFeel.JumpAnticipationMoveScale : 0.2f;
    private float JumpLandingRecoveryTime => jumpFeel != null ? jumpFeel.JumpLandingRecoveryTime : 0.15f;
    private float JumpLandingMoveScale => jumpFeel != null ? jumpFeel.JumpLandingMoveScale : 0.1f;
    private float CoyoteTime => inputBuffering != null ? inputBuffering.CoyoteTime : 0.08f;
    private float JumpBufferTime => inputBuffering != null ? inputBuffering.JumpBufferTime : 0.1f;
    private float DashDistance => (dashRule != null && statsData != null) ? LocomotionFormula.ScaleDashDistance(statsData.MoveSpeed, dashRule.DashSpeedMultiplier, dashRule.DashDuration) : 4f;
    private float DashDuration => dashRule != null ? dashRule.DashDuration : 0.16f;
    private float DashCooldown => dashRule != null ? dashRule.DashCooldown : 2f;
    private float DashInvincibleDuration => dashRule != null ? dashRule.DashInvincibleDuration : 0.12f;
    // IDamageable.TakeDamage 등 공격 SO 없이 호출되는 경로에서 쓰이는 fallback 상수.
    private const float FallbackReactionDuration = 0.35f;
    private const float FallbackKnockbackFriction = 14f;
    private const float ChainedHitReactionDurationMultiplier = 0.8f;
    private float WakeupDuration => actionRecovery != null ? actionRecovery.WakeupDuration : 0f;
    private float WakeupInvincibleDuration => actionRecovery != null ? actionRecovery.WakeupInvincibleDuration : 0f;
    private string JumpStartStateName => animationData != null ? animationData.JumpStartStateName : "";
    private string JumpIdleStateName => animationData != null ? animationData.JumpIdleStateName : "";
    private string JumpEndStateName => animationData != null ? animationData.JumpEndStateName : "";
    private string DashStateName => animationData != null ? animationData.DashStateName : "";
    private string HitStateName => animationData != null ? animationData.HitStateName : "";
    private string DownStateName => animationData != null ? animationData.DownStateName : "";
    private string WakeupStateName => animationData != null ? animationData.WakeupStateName : "";
    private string DeathStateName => animationData != null ? animationData.DeathStateName : "";
    private float ActionTransition => animationData != null ? animationData.ActionTransition : 0.05f;
}
