using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MapNav.Ecs;
using UnityEngine;
using UnityEngine.Serialization;

public enum Character_ActionState
{
    Normal = 0,
    Jump = 1,
    Dash = 2,
    Hitstun = 3,
    Knockback = 4,
    Down = 5,
    Wakeup = 6,
    Dead = 7,
    Launched = 8
}

[RequireComponent(typeof(Character_MoveController))]
[RequireComponent(typeof(Character_Animator))]
[RequireComponent(typeof(Character_AttackController))]
[RequireComponent(typeof(Character_Vitals))]
[DisallowMultipleComponent]
public class Character_ActionHandler : LoopMonoBehaviour, IDamageable, IHitTarget
{
    [SerializeField] private SO_Character_Data _playerData;
    [SerializeField] private SO_Character_Loadout equippedLoadout;

    private SO_Character_Stats statsData;
    private SO_Actor_AnimationData animationData;
    private SO_Character_LocomotionFeel locomotionFeel;
    private SO_WorldPhysics worldPhysics;
    private SO_Character_InputBuffering inputBuffering;
    private SO_Character_JumpFeel jumpFeel;
    private SO_Character_DashRule dashRule;
    private SO_ActionRecovery actionRecovery;

    public Character_ActionState State => _state;
    public bool IsInvincible => _invincibleTimer > 0f;
    public bool CanAttack => _state == Character_ActionState.Normal;
    public bool CanUseSkill => _state == Character_ActionState.Normal || _state == Character_ActionState.Jump;
    public bool LocksLocomotion => _state != Character_ActionState.Normal;
    public bool CanEnterSectorGate => _state == Character_ActionState.Dash || Time.time <= _sectorGateDashGraceUntil;
    public float GateTransitionSpeed => dashRule != null ? dashRule.GateTransitionSpeed : 18f;
    public float Health => _vitals != null ? _vitals.Health : 0f;
    public float MaxHealth => _vitals != null ? _vitals.MaxHealth : (statsData != null ? statsData.MaxHealth : 100f);
    public float Gauge => _vitals != null ? _vitals.Gauge : 0f;
    public float GaugeMax => _vitals != null ? _vitals.GaugeMax : (statsData != null ? statsData.GaugeMax : 100f);
    public NavFaction Faction => _vitals != null ? _vitals.Faction : NavFaction.Ally;
    public SO_Skill_Data GetSkillData(int slot)
    {
        if (_attackController != null) return _attackController.GetSkillData(slot);
        SO_Skill_Data[] skills = ActiveLoadout != null ? ActiveLoadout.EquippedSkills : null;
        if (slot < 0 || skills == null || slot >= skills.Length) return null;
        return skills[slot];
    }

    public float GetSkillCooldown(int slot)
        => _attackController != null ? _attackController.GetSkillCooldown(slot) : 0f;

    public float GetSkillCooldownDuration(int slot)
    {
        SO_Skill_Data skill = GetSkillData(slot);
        return skill != null ? skill.Cooldown : 0f;
    }

    // HUD/디버그 패널이 구독한다. 데미지/회복/사망/시작 시점에 (current, max)를 발사.
    // 체력/게이지 권위는 Character_Vitals가 들고, 여기서는 그대로 중계만 한다(Awake 이후 구독).
    public event System.Action<float, float> OnHealthChanged
    {
        add { if (_vitals != null) _vitals.OnHealthChanged += value; }
        remove { if (_vitals != null) _vitals.OnHealthChanged -= value; }
    }
    public event System.Action<float, float> OnGaugeChanged
    {
        add { if (_vitals != null) _vitals.OnGaugeChanged += value; }
        remove { if (_vitals != null) _vitals.OnGaugeChanged -= value; }
    }

    public void PrepareSectorGateTransition()
    {
        _sectorGateDashGraceUntil = 0f;
        _moveController?.StopPlanar();
        _moveController?.StopLunge();
        _forcedDirection = Vector3.zero;
        _forcedSpeed = 0f;
        _stateTimer = 0f;
        PlayAction(DashStateName);
    }

    public void CompleteSectorGateTransition()
    {
        _vfx?.StopDash();
        EnterNormal();
    }

    private Character_MoveController _moveController;
    private Character_AttackController _attackController;
    private Character_Animator _animator;
    private Character_Vfx _vfx;
    private Character_Vitals _vitals;
    private Character_CommandSource _commandSource;
    private Character_ActionState _state;
    private Vector3 _forcedDirection;
    private float _forcedSpeed;
    private float _forcedFriction;
    private float _stateTimer;
    private float _invincibleTimer;
    private float _dashCooldownTimer;
    private float _sectorGateDashGraceUntil;
    private float _hitReactionDurationScale = 1f;
    // launch(공중 부양). 잡몹 NavLaunchSystem과 동일하게 LaunchPhysics(초기속도+중력 적분)로 y를 구동해
    // 궤적·타이밍·체공을 통일한다. _launchGroundY는 최초 진입 시의 지면 y(잡몹 NavAgentLaunch.GroundY와 동형).
    private float _launchVerticalVelocity;
    private float _launchGroundY;
    private float _launchHeight;
    private float _launchSuspendTimer;
    private bool _launchPendingDown;
    private float _launchDownDuration;
    private float _coyoteTimer;
    private float _jumpBufferTimer;
    private bool _jumpLeftGround;
    private bool _jumpLiftStarted;
    private bool _jumpIdlePlayed;
    private bool _jumpEndPlayed;

    private void Awake()
    {
        _moveController = GetComponent<Character_MoveController>();
        _attackController = GetComponent<Character_AttackController>();
        _animator = GetComponent<Character_Animator>();
        _vfx = GetComponent<Character_Vfx>();
        _vitals = GetComponent<Character_Vitals>();
        if (_vitals == null)
            _vitals = gameObject.AddComponent<Character_Vitals>();
        _commandSource = ResolveCommandSource();

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
        SO_Character_Loadout loadout = ActiveLoadout;
        _attackController.SetBasicAttackCombo(loadout != null ? loadout.EquippedAttackCombo : null);
        _attackController.SetSkills(loadout != null ? loadout.EquippedSkills : null);
        _attackController.SetCommandSource(_commandSource);
        _animator.SetCommandSource(_commandSource);

        // 플레이어는 Ally. 엘리트(장수)는 진영·스탯을 Elite_Embodiment.Bind가 Elite_State 기준으로 주입하므로
        // 여기서 Configure하지 않는다. 여기서 잠정 Enemy로 Configure하면 Bind 전 한 프레임 동안
        // Character_EcsBridge가 Enemy로 발행해 아군 엘리트가 입장 순간 아군 잡몹의 적으로 오인된다.
        // Configure 전까지는 Vitals.FactionResolved=false → Bridge가 HasValue=0으로 타겟 후보에서 제외한다.
        if (!TryGetComponent(out Elite_Embodiment _))
        {
            NavFaction faction = TryGetComponent(out Player_Actor _) ? NavFaction.Ally : NavFaction.Enemy;
            _vitals.Configure(
                statsData != null ? statsData.MaxHealth : 100f,
                statsData != null ? statsData.Defense : 0f,
                statsData != null ? statsData.GaugeMax : 100f,
                faction);
        }
        _vitals.OnDied += EnterDead;
    }

    private void OnDestroy()
    {
        if (_vitals != null)
            _vitals.OnDied -= EnterDead;
    }

    private SO_Character_Loadout ActiveLoadout
        => equippedLoadout != null
            ? equippedLoadout
            : (_playerData != null ? _playerData.DefaultLoadout : null);

    public void SetEquippedLoadout(SO_Character_Loadout loadout)
    {
        equippedLoadout = loadout;
        SO_Character_Loadout active = ActiveLoadout;
        _attackController?.SetBasicAttackCombo(active != null ? active.EquippedAttackCombo : null);
        _attackController?.SetSkills(active != null ? active.EquippedSkills : null);
    }

    public void SetCommandSource(Character_CommandSource commandSource)
    {
        _commandSource = commandSource;
        _attackController?.SetCommandSource(commandSource);
        _animator?.SetCommandSource(commandSource);
    }

    protected override void OnGameUpdate(float gdt)
    {
        base.OnGameUpdate(gdt);

        if (_state == Character_ActionState.Dead)
        {
            TickDead(gdt);
            return;
        }

        TickSharedTimers(gdt);
        BufferJumpInput();

        Vector3 worldInput = GetMoveWorld();

        _attackController?.UpdateLookDirection(GetLookWorld(worldInput));

        bool attackPressed = _commandSource != null && _commandSource.ConsumeAttack();
        if (attackPressed && _attackController != null && (_state == Character_ActionState.Normal || _state == Character_ActionState.Jump || _attackController.IsInCombo))
            _attackController.RequestAttack();

        if (_attackController != null && _attackController.BlocksMovement)
        {
            if (_state == Character_ActionState.Dash)
            {
                _vfx?.StopDash();
                _state = Character_ActionState.Normal;
            }
            if (_attackController.IsSlamDescending)
                _moveController.MoveDown(_attackController.SlamDescentSpeed, gdt);
            else if (_attackController.SuspendsAtApex)
                _moveController.MoveVerticalUntilApexThenSuspend(gdt);
            else
                _moveController.MoveVertical(gdt);
            return;
        }

        switch (_state)
        {
            case Character_ActionState.Dash:
                TickForcedMove(gdt, Character_ActionState.Normal);
                break;
            case Character_ActionState.Jump:
                TickJump(gdt);
                break;
            case Character_ActionState.Hitstun:
                TickHitstun(gdt);
                break;
            case Character_ActionState.Knockback:
                TickKnockback(gdt);
                break;
            case Character_ActionState.Down:
                TickDown(gdt);
                break;
            case Character_ActionState.Launched:
                TickLaunched(gdt);
                break;
            case Character_ActionState.Wakeup:
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
        float superArmorBreak = 0f,
        AttackLaunchData launch = default,
        Vector3 attackerForward = default)
    {
        if (IsInvincible || _state == Character_ActionState.Dead) return;

        ApplyDamage(damage);
        if (_state == Character_ActionState.Dead) return;
        AddGauge(statsData != null ? statsData.GaugeGainOnReceive : 0f);
        TriggerHitstop(hitstop);

        if (_attackController != null && _attackController.IsSuperArmoredAgainst(superArmorBreak))
            return;

        float hitReactionDurationScale = ConsumeHitReactionDurationScale();
        float scaledReactionDuration = Mathf.Max(0f, down.duration) * hitReactionDurationScale;
        float scaledKnockbackDuration = Mathf.Max(0f, knockback.duration) * hitReactionDurationScale;
        float stateDuration = Mathf.Max(scaledReactionDuration, scaledKnockbackDuration);

        _attackController?.CancelAttack();
        _vfx?.StopDash();

        // 넉백 방향: Directional이면 공격자 forward, 아니면 피격자→바깥 방사형(ECS HitboxProcessor와 동일 규칙).
        Vector3 radial = transform.position - hitSource;
        radial.y = 0f;
        if (radial.sqrMagnitude <= 0.0001f)
            radial = -transform.forward;
        radial.Normalize();
        Vector3 forward = attackerForward;
        forward.y = 0f;
        Vector3 direction = (knockback.type == KnockbackType.Directional && forward.sqrMagnitude > 0.0001f)
            ? forward.normalized
            : radial;

        // 강한 넉백/튕김이 직후 들어오는 약한 후속타(메인 repeat 등)에 즉시 지워지지 않게 한다.
        // (Taunt 끝 extra force:30 → 직후 메인 force:10이 덮어쓰는 문제 방지)
        float forcedSpeed = Mathf.Max(0f, knockback.force);
        if (IsHitReactionState(_state) && forcedSpeed < _forcedSpeed)
            forcedSpeed = _forcedSpeed;

        _forcedDirection = direction;
        _forcedSpeed = forcedSpeed;
        _forcedFriction = Mathf.Max(0f, knockback.friction);
        _stateTimer = stateDuration;
        _moveController.StopPlanar();

        // launch는 캐릭터를 실제로 공중에 띄운다. 착지(궤적 종료) 후 down이 예약돼 있으면 다운으로 이어진다.
        if (launch.enabled && launch.height > 0f)
        {
            EnterOrRefreshLaunch(launch, down);
            return;
        }

        // launch.enabled가 아니어도, 이미 공중에 떠 있을 때 suspendDuration>0이면 체공 타이머만 갱신해
        // juggle을 유지한다(잡몹 Character_HitboxProcessor와 동일 규칙). 상태를 Knockback/Hitstun으로
        // 떨어뜨리지 않는다 — ComboAttack 2타처럼 launch 없이 suspendDuration만으로 묶는 경우.
        if (_state == Character_ActionState.Launched && launch.suspendDuration > 0f)
        {
            _launchSuspendTimer = launch.suspendDuration;
            // CancelAttack→ReleaseLocomotion이 푼 로코모션 억제를 다시 걸고 피격 모션을 유지한다.
            // (안 하면 공중 상태라 Character_Animator가 Jump_Idle을 재생한다. 잡몹은 매 hit hit애니 재생.)
            PlayHitReaction(HitReactionKind.Launch);
            return;
        }

        if (down.enabled)
        {
            EnterDown(stateDuration);
            return;
        }

        _state = scaledKnockbackDuration > 0f ? Character_ActionState.Knockback : Character_ActionState.Hitstun;
        PlayHitReaction(scaledKnockbackDuration > 0f ? HitReactionKind.HeavyHit : HitReactionKind.LightHit);
    }

    public void ReceiveHit(Vector3 hitSource, SO_Attack_Data attack)
    {
        if (attack == null) return;
        ReceiveHit(
            hitSource,
            attack.Knockback,
            attack.Damage,
            attack.Hitstop,
            attack.Down,
            attack.SuperArmorBreak,
            attack.Launch);
    }

    // IHitTarget: GameObject 공격(Character_AttackController OverlapSphere)이 들어오는 경로.
    // finalDamage는 공격자 쪽에서 이미 공격력 스케일을 적용한 값이라 그대로 사용한다.
    // ReceiveHit이 무시하는 상태(무적·사망)와 동일 조건. 공격자가 이때 시체/무적 대상을 건너뛰어
    // 타격 연출·히트스톱·게이지가 헛으로 들어가지 않게 한다.
    bool IHitTarget.IsHittable => !IsInvincible && _state != Character_ActionState.Dead;

    bool IHitTarget.IsAirborneHittable => _state == Character_ActionState.Launched;

    void IHitTarget.ReceiveHit(Vector3 attackerPos, Vector3 attackerForward, in AttackHitInfo hit, float finalDamage)
    {
        ReceiveHit(attackerPos, hit.Knockback, finalDamage, hit.Hitstop, hit.Down, hit.SuperArmorBreak, hit.Launch, attackerForward);
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

    // 체력/게이지 권위는 Character_Vitals. 방어력 감산·사망 판정(OnDied→EnterDead)은 거기서 처리한다.
    private void ApplyDamage(float amount) => _vitals.ApplyDamage(amount);

    public void Heal(float amount) => _vitals.Heal(amount);

    public void AddGauge(float amount) => _vitals.AddGauge(amount);

    public void AddInvincible(float duration)
    {
        if (duration <= 0f) return;
        _invincibleTimer = Mathf.Max(_invincibleTimer, duration);
    }

    public bool TryConsumeGauge(float cost) => _vitals.TryConsumeGauge(cost);

    private void EnterDead()
    {
        _state = Character_ActionState.Dead;
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
        if (_commandSource != null && _commandSource.ConsumeDash() && _dashCooldownTimer <= 0f)
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
        _state = Character_ActionState.Jump;
        _stateTimer = Mathf.Max(0f, JumpAnticipationTime);
        PlayAction(JumpStartStateName);

        if (_stateTimer <= 0f)
            StartJumpLift();
    }

    private void TickJump(float deltaTime)
    {
        Vector3 worldInput = GetMoveWorld();

        if (_commandSource != null && _commandSource.ConsumeDash() && _dashCooldownTimer <= 0f)
        {
            EnterDash(worldInput, deltaTime);
            return;
        }

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
        _state = Character_ActionState.Dash;
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

    private void TickForcedMove(float deltaTime, Character_ActionState nextState)
    {
        _stateTimer -= deltaTime;
        _moveController.MoveVelocity(_forcedDirection * _forcedSpeed, deltaTime, true);

        if (_stateTimer > 0f) return;

        bool endedDash = _state == Character_ActionState.Dash;
        _state = nextState;
        if (endedDash)
        {
            _sectorGateDashGraceUntil = Time.time + GateEntryGraceDuration;
            _vfx?.PlayDashEnd(_forcedDirection);
        }

        if (_state == Character_ActionState.Normal)
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

    // 공중 부양. _forcedDirection/_forcedSpeed/_forcedFriction은 호출 직전 ReceiveHit에서 넉백 값으로 세팅돼 있다.
    // 잡몹(NavLaunchSystem)과 동일한 LaunchPhysics(초기속도+중력)로 y를 구동한다. repeat 재타격 시
    // 지면 기준(GroundY)은 유지하고 수직속도만 재부여해 체공을 연장한다(잡몹 HitboxProcessor와 동일 규칙).
    private void EnterOrRefreshLaunch(AttackLaunchData launch, AttackDownData down)
    {
        bool wasLaunched = _state == Character_ActionState.Launched;
        if (!wasLaunched)
            _launchGroundY = transform.position.y;

        _state = Character_ActionState.Launched;
        _launchHeight = Mathf.Max(0f, launch.height);
        // launch 중력은 잡몹(NavLaunchSystem)과 반드시 같은 값(LaunchPhysics.Gravity)을 써야 체공시간이
        // 일치한다. 일반 점프/낙하는 worldPhysics.Gravity를 쓰지만 launch는 잡몹과 통일한다.
        float initialVelocity = LaunchPhysics.InitialVelocity(launch.height, LaunchPhysics.Gravity);
        _launchVerticalVelocity = LaunchPhysics.RefreshVelocityForLaunchHit(_launchVerticalVelocity, initialVelocity, wasLaunched);
        _launchSuspendTimer = Mathf.Max(0f, launch.suspendDuration);
        _launchPendingDown = down.enabled;
        _launchDownDuration = Mathf.Max(0f, down.duration);
        _moveController.StopPlanar();
        PlayHitReaction(HitReactionKind.Launch);
    }

    private void TickLaunched(float deltaTime)
    {
        _forcedSpeed *= Mathf.Max(0f, 1f - _forcedFriction * deltaTime);

        float y = transform.position.y;
        float ceiling = _launchGroundY + _launchHeight;
        LaunchPhysics.Integrate(ref y, ref _launchVerticalVelocity, LaunchPhysics.Gravity, deltaTime, ref _launchSuspendTimer, ceiling);

        // 낙하해서 시작 지면 이하로 내려오면 착지. down이 예약돼 있으면 다운으로 이어진다.
        bool landed = _launchVerticalVelocity <= 0f && y <= _launchGroundY;
        if (landed)
            y = _launchGroundY;

        Vector3 displacement = _forcedDirection * (_forcedSpeed * deltaTime);
        displacement.y = y - transform.position.y;
        _moveController.MoveDisplacement(displacement);

        if (!landed)
            return;

        if (_launchPendingDown)
            EnterDown(_launchDownDuration);
        else
            EnterNormal();
    }

    private void EnterDown(float duration)
    {
        _state = Character_ActionState.Down;
        _stateTimer = Mathf.Max(0f, duration);
        _moveController.StopPlanar();
        PlayHitReaction(HitReactionKind.Down);
    }

    private void TickDown(float deltaTime)
    {
        _stateTimer -= deltaTime;
        _forcedSpeed *= Mathf.Max(0f, 1f - _forcedFriction * deltaTime);
        _moveController.MoveVelocity(_forcedDirection * _forcedSpeed, deltaTime, true);

        if (_stateTimer > 0f) return;

        _state = Character_ActionState.Wakeup;
        _stateTimer = WakeupDuration;
        _invincibleTimer = Mathf.Max(_invincibleTimer, WakeupInvincibleDuration);
        PlayHitReaction(HitReactionKind.Wakeup);
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
        _state = Character_ActionState.Normal;
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

    private static bool IsHitReactionState(Character_ActionState state)
        => state == Character_ActionState.Hitstun
           || state == Character_ActionState.Knockback
           || state == Character_ActionState.Down
           || state == Character_ActionState.Launched;

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
        if (_commandSource != null && _commandSource.ConsumeJump())
            _jumpBufferTimer = JumpBufferTime;
    }

    private Character_CommandSource ResolveCommandSource()
    {
        if (TryGetComponent(out Player_Actor _))
        {
            Player_InputCommandSource playerSource = GetComponent<Player_InputCommandSource>();
            return playerSource != null ? playerSource : gameObject.AddComponent<Player_InputCommandSource>();
        }

        return GetComponent<Character_CommandSource>();
    }

    private Vector3 GetMoveWorld()
        => _commandSource != null ? _commandSource.MoveWorld : Vector3.zero;

    private Vector3 GetLookWorld(Vector3 fallback)
    {
        Vector3 look = _commandSource != null ? _commandSource.LookWorld : Vector3.zero;
        return look.sqrMagnitude > 0.0001f ? look : fallback;
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

    // 피격 리액션은 공유 HitReactionPlayer로 kind에 맞는 클립을 재생한다(잡몹과 동일한 진입·실행 메커니즘).
    private void PlayHitReaction(HitReactionKind kind)
    {
        _animator.PlayHitReaction(kind);
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
    private float GateEntryGraceDuration => dashRule != null ? dashRule.GateEntryGraceDuration : 0.35f;
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
