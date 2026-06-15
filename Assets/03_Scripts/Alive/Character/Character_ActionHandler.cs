using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MapNav.Ecs;
using UnityEngine;

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
public partial class Character_ActionHandler : LoopMonoBehaviour, IDamageable, IHitTarget
{
    private SO_Character_Data _characterData;
    private SO_Character_Loadout _equippedLoadout;

    private SO_Character_Stats statsData;
    private SO_Actor_AnimationData animationData;
    private SO_Character_LocomotionFeel locomotionFeel;
    private SO_WorldPhysics worldPhysics;
    private SO_Character_InputBuffering inputBuffering;
    private SO_Character_JumpFeel jumpFeel;
    private SO_Character_DashRule dashRule;
    private SO_ActionRecovery actionRecovery;
    private SO_Character_BreakFeel breakFeel;

    public Character_ActionState State => _state;
    public bool IsInvincible => _invincibleTimer > 0f;
    // "지금 피격당할 수 있나"의 단일 진실. ReceiveHit·IHitTarget.IsHittable·Character_EcsHitReceiver가
    // 제각기 같은 조건을 복사해 들고 있던 것을 하나로 모은다. 한 곳만 고치면 세 경로가 동시에 일관된다.
    // 퍼펙트 닷지 윈도우 중엔 무적이어도 "맞을 수 있음"으로 노출한다 — 그래야 적 타격이 ReceiveHit까지
    // 도달해 퍼펙트 닷지로 전환된다(ReceiveHit이 데미지 대신 반격창을 연다).
    public bool IsHittable => !_sectorGateTransitioning && _state != Character_ActionState.Dead
                              && (!IsInvincible || _perfectDodgeWindowTimer > 0f);
    // 논리적 "공중에 떠 있나"(raw isGrounded 아님). 점프 상승·하강 중과 launch 체공 중 true,
    // 착지 리커버리(_jumpEndPlayed)부터는 false. Character_Animator가 공중 포즈(JumpIdle) 단독 재생에 쓴다.
    // Jump/Launched는 상태로 즉시 판정(이륙 애니 지연 없음). 그 외 상태(에어 대시 후 Normal 낙하 등)는
    // 실제 접지 여부로 판정하되, 착지 직후 raw isGrounded가 1~2프레임 false로 튀는 플리커는 디바운스로 무시.
    public bool IsAirborne => _state == Character_ActionState.Launched
                              || (_state == Character_ActionState.Jump && !_jumpEndPlayed)
                              || _airborneTimer > AirborneFlickerDebounce;
    // 점프 호 식별자. 값이 바뀌면 새 점프(첫/2단)가 시작된 것 → Animator가 공중 포즈를 처음부터 재생.
    public int JumpArcVersion => _jumpArcVersion;
    public bool IsSectorGateTransitioning => _sectorGateTransitioning;
    public bool CanAttack => !_sectorGateTransitioning && _state == Character_ActionState.Normal;
    public bool CanUseSkill => !_sectorGateTransitioning && (_state == Character_ActionState.Normal || _state == Character_ActionState.Jump);
    public bool LocksLocomotion => _sectorGateTransitioning || _state != Character_ActionState.Normal;
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
        _sectorGateTransitioning = true;
        _sectorGateDashGraceUntil = 0f;
        _attackController?.CancelAttack();
        _moveController?.StopPlanar();
        _moveController?.StopLunge();
        _forcedDirection = Vector3.zero;
        _forcedSpeed = 0f;
        _stateTimer = 0f;
        PlayAction(DashStateName);
    }

    public void CompleteSectorGateTransition()
    {
        _sectorGateTransitioning = false;
        _vfx?.StopDash();
        EnterNormal();
        // 도착 섹터에서 잡몹 떼에 둘러싸여 즉사하는 것을 막는 입장 무적. 전환 도중엔 이미 피격 무시이므로 완료 직후에 건다.
        AddInvincible(GatePostTransitionInvincibleDuration);
    }

    private Character_MoveController _moveController;
    private Character_VerticalMotion _vertical;
    private Character_AttackController _attackController;
    private Character_Animator _animator;
    private Character_Vfx _vfx;
    private Character_Vitals _vitals;
    private Character_BreakOutlineController _breakOutline;
    private CharacterController _characterController;
    private Character_CommandSource _commandSource;
    [Header("Debug")]
    [SerializeField, Tooltip("켜면 모든 액션 상태 전이를 콘솔에 찍는다(에디터 전용). '가끔 튀어나오는' 이상 전이 추적용.")]
    private bool _logStateTransitions;
    private Character_ActionState _state;
    private Vector3 _forcedDirection;
    private float _forcedSpeed;
    private float _forcedFriction;
    private float _stateTimer;
    private float _invincibleTimer;
    private float _dashCooldownTimer;
    // 닷지 시작 직후 퍼펙트 윈도우. 이 동안 적 타격이 닿으면 퍼펙트 닷지(데미지 무효+반격창). 그 뒤 반격창.
    private float _perfectDodgeWindowTimer;
    private float _counterWindowTimer;
    private float _sectorGateDashGraceUntil;
    private bool _sectorGateTransitioning;
    private float _hitReactionDurationScale = 1f;
    // launch 수직 적분·궤적·groundY는 Character_VerticalMotion 소유. 여기선 착지 후 다운 예약만 보유.
    private bool _launchPendingDown;
    private float _launchDownDuration;
    private float _coyoteTimer;
    private float _airborneTimer;
    private float _jumpBufferTimer;
    private int _jumpCount;
    private bool _jumpEndPlayed;
    // 점프 호가 시작될 때마다 증가. Character_Animator가 이 변화를 보고 공중 포즈를 처음부터
    // 강제 재생한다(2단 점프 때 같은 JumpIdle 해시라 멱등 Play가 스킵돼 1단 마지막 프레임이 고정되는 것 방지).
    private int _jumpArcVersion;

    private void Awake()
    {
        _moveController = GetComponent<Character_MoveController>();
        _vertical = GetComponent<Character_VerticalMotion>();
        if (_vertical == null)
            _vertical = gameObject.AddComponent<Character_VerticalMotion>();
        _attackController = GetComponent<Character_AttackController>();
        _animator = GetComponent<Character_Animator>();
        _vfx = GetComponent<Character_Vfx>();
        _vitals = GetComponent<Character_Vitals>();
        _breakOutline = GetComponent<Character_BreakOutlineController>();
        _characterController = GetComponent<CharacterController>();
        if (_vitals == null)
            _vitals = gameObject.AddComponent<Character_Vitals>();
        if (_breakOutline == null)
            _breakOutline = gameObject.AddComponent<Character_BreakOutlineController>();
        // 기본 커맨드 소스 = 프리팹에 구워진 자율 소스(Elite_AICommandSource). 플레이어 빙의는
        // PlayerController가 SetCommandSource로 플레이어 입력을 덮어쓴다(automode면 다시 AI 소스로 환원).
        _commandSource = GetComponent<Character_CommandSource>();

        ApplyCharacterData(_characterData);
        _attackController.SetCommandSource(_commandSource);
        _animator.SetCommandSource(_commandSource);

        // 진영/스탯(Vitals)은 주입자가 ConfigureVitals로 넣는다: 플레이어=PlayerController.Possess(Ally),
        // 엘리트=Elite_Embodiment.Bind(Elite_State 기준). 그 전까지 Vitals.FactionResolved=false →
        // Character_EcsBridge가 "진영 미확정"으로 보고 잡몹 타겟·타격 후보에서 제외한다(입장 첫 프레임 오인 방지).
        _vitals.OnDied += EnterDead;
    }

    private void OnDestroy()
    {
        if (_vitals != null)
            _vitals.OnDied -= EnterDead;
    }

    private SO_Character_Loadout ActiveLoadout
        => _equippedLoadout != null
            ? _equippedLoadout
            : (_characterData != null ? _characterData.DefaultLoadout : null);

    public void SetCharacterData(SO_Character_Data characterData, bool clearEquippedLoadout = false)
    {
        _characterData = characterData;
        if (clearEquippedLoadout)
            _equippedLoadout = null;
        ApplyCharacterData(_characterData);
    }

    public void SetEquippedLoadout(SO_Character_Loadout loadout)
    {
        _equippedLoadout = loadout;
        ApplyActiveLoadout();
    }

    private void ApplyCharacterData(SO_Character_Data characterData)
    {
        statsData = characterData != null ? characterData.StatsData : null;
        animationData = characterData != null ? characterData.AnimationData : null;
        locomotionFeel = characterData != null ? characterData.LocomotionFeel : null;
        worldPhysics = characterData != null ? characterData.WorldPhysics : null;
        inputBuffering = characterData != null ? characterData.InputBuffering : null;
        jumpFeel = characterData != null ? characterData.JumpFeel : null;
        dashRule = characterData != null ? characterData.DashRule : null;
        actionRecovery = characterData != null ? characterData.ActionRecovery : null;
        breakFeel = characterData != null ? characterData.BreakFeel : null;

        _moveController?.SetMovementData(statsData, locomotionFeel, worldPhysics);
        _animator?.SetAnimationData(animationData);
        _attackController?.SetPlayerStats(statsData);
        _breakOutline?.SetBrokenRenderingLayerMask(BreakOutlineRenderingLayerMask);
        ApplyActiveLoadout();
    }

    // 진영·스탯을 Vitals에 주입한다(진영 주입 = FactionResolved 게이트 해제). 호출자가 진영을 정한다:
    // 플레이어=PlayerController.Possess(Ally), 엘리트=Elite_Embodiment.Bind(Elite_State.Faction, 직전 체력).
    // 스탯/브레이크 값은 SetCharacterData로 적용된 자기 데이터(statsData/breakFeel)에서 온다.
    public void ConfigureVitals(NavFaction faction, float? startHealth = null)
    {
        if (_vitals == null)
            return;

        _vitals.Configure(
            statsData != null ? statsData.MaxHealth : 100f,
            statsData != null ? statsData.Defense : 0f,
            statsData != null ? statsData.GaugeMax : 100f,
            faction,
            startHealth,
            bodyRadius: statsData != null ? statsData.BodyRadius : 0.5f,
            breakMax: statsData != null ? statsData.BreakMax : 0f,
            breakRecoveryDelay: BreakRecoveryDelay,
            breakRecoveryPerSecond: BreakRecoveryPerSecond,
            brokenDuration: BreakVulnerableDuration,
            breakRecoveryRatioOnBrokenEnd: BreakRecoveryRatioOnBrokenEnd);
    }

    private void ApplyActiveLoadout()
    {
        SO_Character_Loadout loadout = ActiveLoadout;
        _attackController?.SetBasicAttackCombo(loadout != null ? loadout.EquippedAttackCombo : null);
        _attackController?.SetSkills(loadout != null ? loadout.EquippedSkills : null);
        _attackController?.SetCounterAttack(loadout != null ? loadout.CounterAttack : null);
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

        if (_sectorGateTransitioning)
        {
            _commandSource?.ConsumeAttack();
            _commandSource?.ConsumeJump();
            _commandSource?.ConsumeDash();
            for (int i = 0; i < SkillInput.SlotCount; i++)
                _commandSource?.ConsumeSkill(i);
            _moveController.MoveVertical(gdt);
            return;
        }

        BufferJumpInput();

        Vector3 worldInput = GetMoveWorld();

        _attackController?.UpdateLookDirection(GetLookWorld(worldInput));

        bool attackPressed = _commandSource != null && _commandSource.ConsumeAttack();
        if (attackPressed && _attackController != null)
        {
            // 키 압축: 퍼펙트 닷지 직후 반격창이 열려 있으면 같은 공격 버튼이 '반격기'가 된다.
            if (_counterWindowTimer > 0f)
            {
                _counterWindowTimer = 0f;
                // 닷지 중이면 먼저 닷지를 정리한 뒤 반격을 시작한다. 순서가 반대면, 같은 프레임의
                // BlocksMovement 블록이 EnterNormal→ReleaseLocomotion으로 방금 켠 _isAttacking을 도로 꺼서
                // 반격 애니가 죽고 로코모션/공중 포즈가 duration 동안 굳는다.
                if (_state == Character_ActionState.Dash)
                {
                    _vfx?.StopDash();
                    EnterNormal();
                }
                _attackController.TriggerCounter();
            }
            else if (_state == Character_ActionState.Normal || _state == Character_ActionState.Jump || _attackController.IsInCombo)
            {
                _attackController.RequestAttack();
            }
        }

        if (_attackController != null && _attackController.BlocksMovement)
        {
            if (_state == Character_ActionState.Dash)
            {
                _vfx?.StopDash();
                EnterNormal();
            }

            // 닷지 캔슬: 공격 중에도 닷지로 끊고 빠져나간다(반응성 + 반격 루프 진입 — 공격하다 적 윈드업을 보고
            // 닷지로 회피). 닷지의 퍼펙트 윈도우가 곧바로 열려 퍼펙트 닷지→반격으로 이어진다.
            // CancelAttack이 ResetCombo까지 하므로 다음 프레임 BlocksMovement가 풀려 닷지가 정상 진행된다.
            if (_commandSource != null && _commandSource.ConsumeDash() && _dashCooldownTimer <= 0f)
            {
                _attackController.CancelAttack();
                EnterDash(GetMoveWorld(), gdt);
                return;
            }

            if (_attackController.IsSlamDescending)
            {
                CompleteBlockedActionJumpLandingIfGrounded();
                return;
            }
            else if (_attackController.SuspendsAtApex)
                _vertical.SuspendAtApexMove(gdt);
            else
                _moveController.MoveVertical(gdt);
            CompleteBlockedActionJumpLandingIfGrounded();
            return;
        }

        // 콤보 윈도우 중 지상 이동 허용. 단 Normal일 때만 — Jump 상태에서 이걸로 빠지면 TickJump(공중 이동·
        // 착지 완료)가 통째로 건너뛰어져, 공중 공격 후 그냥 내려오면 착지 처리가 안 돼 JumpIdle에 멈춘다.
        if (_attackController != null && _attackController.IsComboWindowOpen && _state == Character_ActionState.Normal)
        {
            _moveController.TickLocomotion(worldInput, gdt);
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
        AttackTimeScaleData hitstop = default,
        AttackDownData down = default,
        float superArmorBreak = 0f,
        float breakGaugeDamage = 0f,
        AttackLaunchData launch = default,
        Vector3 attackerForward = default)
    {
        // 퍼펙트 닷지: 닷지 시작 직후 윈도우 중 적 타격이 닿으면 데미지를 무효화하고 반격창을 연다.
        if (_perfectDodgeWindowTimer > 0f)
        {
            TriggerPerfectDodge();
            return;
        }
        if (!IsHittable) return;

        bool alreadyBroken = _vitals != null && _vitals.IsBroken;
        float resolvedDamage = alreadyBroken ? damage * BrokenDamageTakenMultiplier : damage;
        ApplyDamage(resolvedDamage);
        if (_state == Character_ActionState.Dead) return;
        // 그로기 게이지는 체력 데미지와 독립 — hitResult.breakGaugeDamage만큼 깎인다(인터럽트용 superArmorBreak와 별개).
        bool broken = _vitals != null && _vitals.ApplyBreakDamage(Mathf.Max(0f, breakGaugeDamage));
        AddGauge(statsData != null ? statsData.GaugeGainOnReceive : 0f);
        // 전역 히트스톱(슬로모)은 "로컬 플레이어가 때렸을 때"만 — 공격자측(CombatOnHit, IsLocalPlayer 게이트)이 담당한다.
        // 피격자측에서 걸면 적이 플레이어/유닛을 때릴 때도 시간이 멈추므로(ECS 잡몹 공격 포함) 여기선 걸지 않는다.

        // 브레이크 "터진 순간" 1회 연출은 아래 반응 라우팅보다 먼저, 여기서 발사한다.
        // (공중 저글링 중 브레이크는 juggle 유지 분기에서 일찍 return하므로 그 뒤에 두면 큐가 누락된다.)
        if (broken)
            PlayBreakFeedback();

        if (!IsSuperArmorDisabledByBreak(broken) && _attackController != null && _attackController.IsSuperArmoredAgainst(superArmorBreak))
            return;

        float hitReactionDurationScale = ConsumeHitReactionDurationScale();
        float stateDuration = Mathf.Max(0f, down.duration) * hitReactionDurationScale;

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

        bool incomingLaunch = launch.enabled && launch.height > 0f;
        if (_state == Character_ActionState.Launched && !incomingLaunch)
        {
            if (down.enabled)
            {
                _launchPendingDown = true;
                _launchDownDuration = Mathf.Max(_launchDownDuration, stateDuration);
            }

            _vertical.RefreshLaunchSuspend(launch.suspendDuration);

            // CancelAttack→ReleaseLocomotion이 푼 로코모션 억제를 다시 걸고 피격 모션을 유지한다.
            // (안 하면 공중 상태라 Character_Animator가 Jump_Idle을 재생한다. 잡몹은 매 hit hit애니 재생.)
            // launch 없이 suspendDuration만으로 묶는 경우(ComboAttack 2타 등)도 여기서 함께 처리한다.
            PlayHitReaction(HitReactionKind.Launch);
            return;
        }

        // 강한 넉백/튕김이 직후 들어오는 약한 후속타(메인 repeat 등)에 즉시 지워지지 않게 한다.
        // (Taunt 끝 extra force:30 → 직후 메인 force:10이 덮어쓰는 문제 방지)
        // 음수 force = "당김": 방향을 공격자 쪽(radial 반전)으로 뒤집고 크기는 절댓값으로 쓴다.
        // 캐릭터 넉백은 방향+양수 스칼라 모델이라 음수를 부호로 못 싣는다(ECS는 dir*force라 음수 그대로 안쪽).
        // 절댓값을 쓰므로 아래 "강한 넉백 보존"(magnitude 비교)·감쇠도 그대로 일관된다.
        if (knockback.force < 0f)
            direction = -direction;
        float forcedSpeed = Mathf.Abs(knockback.force);
        if (!alreadyBroken && IsHitReactionState(_state) && forcedSpeed < _forcedSpeed)
            forcedSpeed = _forcedSpeed;

        _forcedDirection = direction;
        _forcedSpeed = forcedSpeed;
        _forcedFriction = Mathf.Max(0f, knockback.friction);
        _stateTimer = stateDuration;
        _moveController.StopPlanar();

        if (alreadyBroken && !incomingLaunch && !down.enabled)
        {
            PlayHitReaction(HitReactionKind.HeavyHit);
            return;
        }

        // launch는 캐릭터를 실제로 공중에 띄운다. 착지(궤적 종료) 후 down이 예약돼 있으면 다운으로 이어진다.
        if (incomingLaunch)
        {
            EnterOrRefreshLaunch(launch, down);
            return;
        }

        if (down.enabled)
        {
            EnterDown(stateDuration);
            return;
        }

        bool hasKnockback = Mathf.Abs(knockback.force) > 0f; // 음수(당김)도 넉백 상태로 — forced 속도가 TickKnockback에서 소비돼야 당겨진다.
        SetState(hasKnockback ? Character_ActionState.Knockback : Character_ActionState.Hitstun);
        PlayHitReaction(hasKnockback ? HitReactionKind.HeavyHit : HitReactionKind.LightHit);
    }

    // IHitTarget: GameObject 공격(Character_AttackController OverlapSphere)이 들어오는 경로.
    // finalDamage는 공격자 쪽에서 이미 공격력 스케일을 적용한 값이라 그대로 사용한다.
    // ReceiveHit이 무시하는 상태(무적·사망)와 동일 조건. 공격자가 이때 시체/무적 대상을 건너뛰어
    // 타격 연출·히트스톱·게이지가 헛으로 들어가지 않게 한다.
    bool IHitTarget.IsHittable => IsHittable;

    bool IHitTarget.IsAirborneHittable => _state == Character_ActionState.Launched;

    void IHitTarget.ReceiveHit(Vector3 attackerPos, Vector3 attackerForward, in AttackHitInfo hit, float finalDamage)
    {
        ReceiveHit(attackerPos, hit.Knockback, finalDamage, hit.Hitstop, hit.Down, hit.SuperArmorBreak, hit.BreakGaugeDamage, hit.Launch, attackerForward);
    }

    private void TriggerHitstop(AttackTimeScaleData hitstop)
    {
        if (hitstop.duration <= 0f || Main.Loop == null) return;
        DoHitstop(hitstop, destroyCancellationToken).Forget();
    }

    // 퍼펙트 닷지 성공: 데미지를 무효화하고(이미 return) 반격창을 연다 + 짧은 슬로모/무적으로 반격 입력 여유를 준다.
    private void TriggerPerfectDodge()
    {
        _perfectDodgeWindowTimer = 0f;
        _counterWindowTimer = CounterWindow;
        _invincibleTimer = Mathf.Max(_invincibleTimer, PerfectDodgeInvincibleDuration);
        if (Main.Loop != null)
            DoPerfectDodgeSlowMo(PerfectDodgeTimeScale, PerfectDodgeSlowMoDuration, destroyCancellationToken).Forget();
    }

    private static async UniTaskVoid DoPerfectDodgeSlowMo(float timeScale, float duration, CancellationToken token)
    {
        Main.Loop.SetGameSpeed(Mathf.Clamp01(timeScale));
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(duration), DelayType.UnscaledDeltaTime, cancellationToken: token);
        }
        catch (OperationCanceledException) { return; }
        if (Main.Loop != null)
            Main.Loop.SetGameSpeed(1f);
    }

    private bool IsSuperArmorDisabledByBreak(bool brokenByThisHit)
        => brokenByThisHit
           || (_vitals != null && _vitals.IsBroken);

    private static async UniTaskVoid DoHitstop(AttackTimeScaleData hitstop, CancellationToken token)
    {
        Main.Loop.SetGameSpeed(Mathf.Clamp01(hitstop.timeScale));
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(hitstop.duration), DelayType.UnscaledDeltaTime, cancellationToken: token);
        }
        catch (OperationCanceledException) { return; }
        if (Main.Loop != null)
            Main.Loop.SetGameSpeed(1f);
    }

    public void TakeDamage(float amount, Vector3 hitSource, float knockbackForce = 0f)
    {
        ReceiveHit(hitSource, new AttackKnockbackData
        {
            force = knockbackForce,
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
        SetState(Character_ActionState.Dead);
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

    private void EnterDash(Vector3 input, float deltaTime)
    {
        Vector3 direction = ResolveActionDirection(input);
        float duration = Mathf.Max(0.01f, DashDuration);
        SetState(Character_ActionState.Dash);
        _stateTimer = duration;
        _forcedDirection = direction;
        _forcedSpeed = DashDistance / duration;
        _invincibleTimer = Mathf.Max(_invincibleTimer, DashInvincibleDuration);
        _perfectDodgeWindowTimer = PerfectDodgeWindow;
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
        SetState(nextState);
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

    private void EnterDown(float duration)
    {
        SetState(Character_ActionState.Down);
        _stateTimer = Mathf.Max(0f, duration);
        _moveController.StopPlanar();
        PlayHitReaction(HitReactionKind.Down);
    }

    private void PlayBreakFeedback()
    {
        App.ShakeCamera(ResolvedBreakShakeAmplitude, ResolvedBreakShakeDuration, ResolvedBreakShakeFrequency);
        Game.PlayCameraCutIn(ResolvedBreakCameraCue);

        float hitstopDuration = ResolvedBreakHitstopDuration;
        if (hitstopDuration > 0f)
        {
            TriggerHitstop(new AttackTimeScaleData
            {
                duration = hitstopDuration,
                timeScale = ResolvedBreakHitstopTimeScale
            });
        }
    }

    private void TickDown(float deltaTime)
    {
        _stateTimer -= deltaTime;
        _forcedSpeed *= Mathf.Max(0f, 1f - _forcedFriction * deltaTime);
        _moveController.MoveVelocity(_forcedDirection * _forcedSpeed, deltaTime, true);

        if (_stateTimer > 0f) return;

        EnterWakeup();
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
        SetState(Character_ActionState.Normal);
        _hitReactionDurationScale = 1f;
        _jumpCount = 0;
        _animator.ReleaseLocomotion();
    }

    private void EnterWakeup()
    {
        SetState(Character_ActionState.Wakeup);
        _stateTimer = WakeupDuration;
        _invincibleTimer = Mathf.Max(_invincibleTimer, WakeupInvincibleDuration);
        PlayHitReaction(HitReactionKind.Wakeup);
    }

    // 모든 _state 쓰기는 반드시 이 한 곳을 통과한다(직접 _state = 대입 금지).
    // 전이가 한 군데로 모여야 _logStateTransitions로 전체 전이 흐름을 한 줄씩 관찰할 수 있고,
    // "어느 경로로 들어가면 이상" 류의 암묵 전이를 추적·검증할 수 있다.
    private void SetState(Character_ActionState next)
    {
#if UNITY_EDITOR
        if (_logStateTransitions && next != _state)
            Debug.Log($"[ActionHandler] {name}: {_state} → {next}", this);
#endif
        _state = next;
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
        _perfectDodgeWindowTimer = Mathf.Max(0f, _perfectDodgeWindowTimer - deltaTime);
        _counterWindowTimer = Mathf.Max(0f, _counterWindowTimer - deltaTime);

        _coyoteTimer = _moveController.IsGrounded
            ? CoyoteTime
            : Mathf.Max(0f, _coyoteTimer - deltaTime);

        // 접지 시 0, 공중이면 누적. 착지 직후 1~2프레임 isGrounded 플리커가 IsAirborne을 오작동시키지 않게
        // 디바운스 임계(AirborneFlickerDebounce)를 넘겨야 "진짜 공중"으로 친다.
        _airborneTimer = _moveController.IsGrounded ? 0f : _airborneTimer + deltaTime;
    }

    private void BufferJumpInput()
    {
        if (_commandSource != null && _commandSource.ConsumeJump())
            _jumpBufferTimer = JumpBufferTime;
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
    private float JumpLandingRecoveryTime => jumpFeel != null ? jumpFeel.JumpLandingRecoveryTime : 0.15f;
    private float JumpLandingMoveScale => jumpFeel != null ? jumpFeel.JumpLandingMoveScale : 0.1f;
    private int MaxJumpCount => jumpFeel != null ? Mathf.Max(1, jumpFeel.MaxJumpCount) : 2;
    private float JumpRiseTime => jumpFeel != null ? Mathf.Max(0.01f, jumpFeel.JumpRiseTime) : 0.1f;
    private float JumpApexHoldTime => jumpFeel != null ? Mathf.Max(0f, jumpFeel.JumpApexHoldTime) : 0.02f;
    private float JumpAirMoveScale => jumpFeel != null ? jumpFeel.JumpAirMoveScale : 1f;
    private float JumpAscentDuration => JumpRiseTime + JumpApexHoldTime;
    private float CoyoteTime => inputBuffering != null ? inputBuffering.CoyoteTime : 0.08f;
    private float JumpBufferTime => inputBuffering != null ? inputBuffering.JumpBufferTime : 0.1f;
    private float DashDistance => (dashRule != null && statsData != null) ? LocomotionFormula.ScaleDashDistance(statsData.MoveSpeed, dashRule.DashSpeedMultiplier, dashRule.DashDuration) : 4f;
    private float DashDuration => dashRule != null ? dashRule.DashDuration : 0.16f;
    private float DashCooldown => dashRule != null ? dashRule.DashCooldown : 2f;
    private float DashInvincibleDuration => dashRule != null ? dashRule.DashInvincibleDuration : 0.12f;
    private float GateEntryGraceDuration => dashRule != null ? dashRule.GateEntryGraceDuration : 0.35f;
    private float GatePostTransitionInvincibleDuration => dashRule != null ? dashRule.GatePostTransitionInvincibleDuration : 1f;
    // IDamageable.TakeDamage 등 공격 SO 없이 호출되는 경로에서 쓰이는 fallback 상수.
    private const float FallbackReactionDuration = 0.35f;
    private const float FallbackKnockbackFriction = 14f;
    private const float ChainedHitReactionDurationMultiplier = 0.8f;
    // 착지 직후 raw isGrounded가 1~2프레임 false로 튀는 것을 무시하는 공중 판정 디바운스(초).
    private const float AirborneFlickerDebounce = 0.06f;
    // 반응 루프(닷지→퍼펙트→반격) 튜닝값은 SO_Character_DashRule에서 온다(fallback은 기본값).
    private float PerfectDodgeWindow => dashRule != null ? dashRule.PerfectDodgeWindow : 0.15f;
    private float CounterWindow => dashRule != null ? dashRule.CounterWindow : 0.6f;
    private float PerfectDodgeInvincibleDuration => dashRule != null ? dashRule.PerfectDodgeInvincibleDuration : 0.2f;
    private float PerfectDodgeTimeScale => dashRule != null ? dashRule.PerfectDodgeTimeScale : 0.4f;
    private float PerfectDodgeSlowMoDuration => dashRule != null ? dashRule.PerfectDodgeSlowMoDuration : 0.3f;
    private float WakeupDuration => actionRecovery != null ? actionRecovery.WakeupDuration : 0f;
    private float WakeupInvincibleDuration => actionRecovery != null ? actionRecovery.WakeupInvincibleDuration : 0f;
    private float BreakVulnerableDuration => breakFeel != null ? breakFeel.BrokenDuration : 1.5f;
    private float BrokenDamageTakenMultiplier => breakFeel != null ? Mathf.Max(1f, breakFeel.BrokenDamageTakenMultiplier) : 1.35f;
    private float BreakRecoveryRatioOnBrokenEnd => breakFeel != null ? breakFeel.RecoveryRatioOnBrokenEnd : 1f;
    private float BreakRecoveryDelay => breakFeel != null ? breakFeel.RecoveryDelay : 1.5f;
    private float BreakRecoveryPerSecond => breakFeel != null ? breakFeel.RecoveryPerSecond : 60f;
    private float ResolvedBreakShakeAmplitude => breakFeel != null ? breakFeel.ShakeAmplitude : 0.38f;
    private float ResolvedBreakShakeDuration => breakFeel != null ? breakFeel.ShakeDuration : 0.32f;
    private float ResolvedBreakShakeFrequency => breakFeel != null ? breakFeel.ShakeFrequency : 36f;
    private float ResolvedBreakHitstopDuration => breakFeel != null ? breakFeel.HitstopDuration : 0.2f;
    private float ResolvedBreakHitstopTimeScale => breakFeel != null ? breakFeel.HitstopTimeScale : 0.01f;
    private SkillCutInData ResolvedBreakCameraCue => breakFeel != null ? breakFeel.CameraCue : default;
    private uint BreakOutlineRenderingLayerMask => breakFeel != null ? breakFeel.BrokenOutlineRenderingLayerMask : 8u;
    private string DashStateName => animationData != null ? animationData.DashStateName : "";
    private string DeathStateName => animationData != null ? animationData.DeathStateName : "";
    private float ActionTransition => animationData != null ? animationData.ActionTransition : 0.05f;
}
