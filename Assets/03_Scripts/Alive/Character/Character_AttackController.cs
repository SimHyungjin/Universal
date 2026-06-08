using System;
using Cysharp.Threading.Tasks;
using MapNav.Ecs;
using Unity.Entities;
using UnityEngine;

[DisallowMultipleComponent]
public partial class Character_AttackController : LoopMonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool drawAttackGizmosAlways;
    [SerializeField] private int gizmoAttackIndex;
    [SerializeField] private Color attackGizmoColor = new Color(1f, 0.2f, 0.2f, 0.35f);


    public bool IsAttacking => _attackTimer > 0f;
    public bool IsInCombo   => _attackTimer > 0f || _comboTimer > 0f;
    public bool IsComboWindowOpen => _attackTimer <= 0f && _comboTimer > 0f;
    public bool IsSkillSequenceActive => _skillSequence != null;
    public bool SuspendsAtApex => IsAttacking && _currentData != null && _currentData.Jump.suspendAtApex
                                  && _currentData.Lunge.moveType != AttackMoveType.Slam;
    public bool IsSlamDescending => _slamDescending && _currentData != null && _currentData.Lunge.moveType == AttackMoveType.Slam;
    public bool BlocksMovement => _attackTimer > 0f || (_comboTimer > 0f && _lockMovementDuringComboWindow);
    public bool IsSuperArmoredAgainst(float superArmorBreak)
        => IsAttacking && _currentData != null && _currentData.SuperArmor > superArmorBreak;

    private Character_Animator       _playerAnimator;
    private Character_MoveController _moveController;
    private Character_VerticalMotion _vertical;
    private Character_Vfx            _vfx;
    private Character_ActionHandler  _actionHandler;
    private SO_Character_Stats      _playerStats;
    private float                  _attackPower;
    private SO_Attack_Data[] _attacks;
    private SO_Attack_Data _counterAttack;
    private float _comboWindow = 0.35f;
    private bool _lockMovementDuringComboWindow = true;
    private const float ExplicitLookInputSqrThreshold = 0.04f;
    private int   _comboCount;
    private float _attackTimer;
    private float _comboTimer;
    private bool  _nextQueued;
    private Vector3       _pendingLookDirection;
    private SO_Attack_Data _currentData;
    private bool          _hitboxFired;
    private int           _hitboxFireCount;
    private float         _nextHitboxElapsed;
    private bool          _attackMoveVfxPlaying;
    private AutoDespawn   _castVfxInstance;
    private System.Threading.CancellationTokenSource _castVfxSpawnCts;
    private System.Threading.CancellationTokenSource _releaseEffectsCts;
    private bool          _slamLandingFired;
    private bool          _slamDescending;

    // Skills (loadout). 4媛??щ’ ??[[project_skill_loadout]]. ?먯썝 ?쒖뒪?쒖? 誘멸뎄?꾩씠??cost???쇰떒 臾댁떆.
    private SO_Skill_Data[] _skills;
    private readonly float[] _skillCooldowns = new float[SkillInput.SlotCount];
    private SO_Attack_Data[] _skillSequence;
    private SO_Skill_Data   _skillData;
    private int             _skillSequenceIndex;
    private bool  _skillSequenceAnyHit;
    private bool _hitCameraCuePlayed;
    private bool[]  _extraHitFired;
    private float[] _extraNextHitboxElapsed;
    private readonly AttackHitRegistry _attackHitRegistry = new();

    // 근접·발사체·장판이 공유하는 히트 판정 구현(GameObject + ECS).
    private readonly AttackHitEmitter _emitter = new();
    private Character_EcsBridge _ecsBridge;
    private Character_CommandSource _commandSource;
    private bool _drivesCameraFollowAlignment;

    private void Awake()
    {
        _playerAnimator = GetComponent<Character_Animator>();
        _moveController = GetComponent<Character_MoveController>();
        _vertical = GetComponent<Character_VerticalMotion>();
        if (_vertical == null)
            _vertical = gameObject.AddComponent<Character_VerticalMotion>();
        _vfx = GetComponent<Character_Vfx>();
        _actionHandler = GetComponent<Character_ActionHandler>();
        _ecsBridge = GetComponent<Character_EcsBridge>();
        _commandSource = GetComponent<Character_CommandSource>();
        _drivesCameraFollowAlignment = GetComponent<Player_Actor>() != null;
    }

    public void SetCommandSource(Character_CommandSource commandSource)
    {
        _commandSource = commandSource;
    }

    public void SetPlayerStats(SO_Character_Stats stats)
    {
        _playerStats = stats;
        _attackPower = stats != null ? stats.AttackPower : 0f;
    }

    // 공격력 출처. 플레이어/장수 모두 SO_Character_Stats.AttackPower에서 온다(장수는 Elite_Brain.Bind가 자기 character로 주입).
    // SetPlayerStats 이후에 호출돼야 적용된다.
    public void SetAttackPower(float attackPower) => _attackPower = Mathf.Max(0f, attackPower);

    // GameObject(IHitTarget) 공격 경로의 진영 필터. 공격자와 다른 진영만 타격한다.
    // 자기 자신은 같은 진영이라 자동 제외된다. Vitals 없는 IHitTarget(파괴물 등)은 항상 허용.
    private NavFaction AttackerFaction => _actionHandler != null ? _actionHandler.Faction : NavFaction.Ally;

    private bool IsHostileHitTarget(Collider col)
        => !col.TryGetComponent(out Character_Vitals targetVitals) || targetVitals.Faction != AttackerFaction;

    public void SetBasicAttackCombo(SO_Attack_ComboData combo)
    {
        if (combo == null)
        {
            _attacks = null;
            _comboWindow = 0.35f;
            _lockMovementDuringComboWindow = true;
            return;
        }

        _attacks = combo.Attacks;
        _comboWindow = combo.ComboWindow;
        _lockMovementDuringComboWindow = combo.LockMovementDuringComboWindow;
    }

    public void SetSkills(SO_Skill_Data[] skills)
    {
        _skills = skills;
        for (int i = 0; i < _skillCooldowns.Length; i++)
            _skillCooldowns[i] = 0f;
    }

    // 퍼펙트 닷지 반격기를 로드아웃에서 주입한다(콤보·스킬과 동일 경로). 비우면 TriggerCounter가 기본 콤보 1타로 폴백.
    public void SetCounterAttack(SO_Attack_Data counterAttack)
    {
        _counterAttack = counterAttack;
    }

    public SO_Skill_Data GetSkillData(int slot)
    {
        if (slot < 0 || _skills == null || slot >= _skills.Length) return null;
        return _skills[slot];
    }

    public float GetSkillCooldown(int slot)
        => slot >= 0 && slot < _skillCooldowns.Length ? _skillCooldowns[slot] : 0f;

    public bool TryTriggerSkill(int slot)
    {
        if (slot < 0 || slot >= _skillCooldowns.Length) return false;
        if (_skills == null || slot >= _skills.Length) return false;
        if (_skillSequence != null) return false;
        if (_actionHandler != null && !_actionHandler.CanUseSkill) return false;
        SO_Skill_Data skill = _skills[slot];
        if (skill == null) return false;
        if (_skillCooldowns[slot] > 0f) return false;

        SO_Attack_Data[] sequence = skill.AttackSequence;
        if (!skill.HasAttackSequence) return false;

        if (skill.IsUltimate)
        {
            if (_actionHandler == null || !_actionHandler.TryConsumeGauge(skill.ResourceCost))
                return false;
            _actionHandler.AddInvincible(skill.InvincibleDuration);
            Game.PlayUltimateOverlay(skill.Overlay);
        }

        // 吏꾪뻾 以?怨듦꺽/肄ㅻ낫媛 ?덉쑝硫??딄퀬 ?ㅽ궗 吏꾩엯. CancelAttack??_skillSequence瑜?鍮꾩슦誘濡?洹??ㅼ뿉 ?ㅼ젙.
        if (_attackTimer > 0f)
            CancelAttack();

        _skillCooldowns[slot] = skill.Cooldown;
        _skillSequence = sequence;
        _skillData = skill;
        _skillSequenceIndex = 0;
        StartAttackDataWithEffects(sequence[0]);
        return true;
    }

    public bool RequestAttack()
    {
        if (_actionHandler != null && _actionHandler.IsSectorGateTransitioning)
            return false;

        if (_attackTimer > 0f)
        {
            _nextQueued = true;
            return true;
        }

        if (_comboCount > 0 && _comboTimer <= 0f)
            ResetCombo();

        _nextQueued = false;
        StartAttack();
        return IsAttacking;
    }

    protected override void OnGameUpdate(float gdt)
    {
        base.OnGameUpdate(gdt);

        TickSkillCooldowns(gdt);
        if (_actionHandler != null && _actionHandler.IsSectorGateTransitioning)
        {
            PollAndDiscardSkillInput();
            if (IsAttacking || IsInCombo || _skillSequence != null)
                CancelAttack();
            return;
        }

        PollSkillInput();

        if (_attackTimer > 0f)
        {
            if (!_slamDescending)
                _attackTimer -= gdt;

            TickAttackHitbox(_currentData, gdt);

            if (_attackTimer <= 0f) OnAttackEnd();
            return;
        }

        if (_nextQueued)
        {
            _nextQueued = false;
            StartAttack();
            return;
        }

        if (_comboTimer > 0f)
        {
            _comboTimer -= gdt;
            if (_comboTimer <= 0f)
                ResetCombo();
        }
    }

    private void TickSkillCooldowns(float gdt)
    {
        for (int i = 0; i < _skillCooldowns.Length; i++)
        {
            if (_skillCooldowns[i] > 0f)
                _skillCooldowns[i] = Mathf.Max(0f, _skillCooldowns[i] - gdt);
        }
    }

    private void PollSkillInput()
    {
        // ?ㅽ궗 ?쒗??吏꾪뻾 以묒뿉???ㅻⅨ ?ㅽ궗??諛쏆? ?딅뒗??(?먭린 ?먯떊 ?딄? 諛⑹?).
        if (_skillSequence != null) return;
        if (_skills == null) return;

        for (int i = 0; i < _skillCooldowns.Length; i++)
        {
            if (!ConsumeSkill(i)) continue;
            if (TryTriggerSkill(i)) return;
        }
    }

    private bool ConsumeSkill(int slot)
    {
        _commandSource ??= GetComponent<Character_CommandSource>();
        return _commandSource != null && _commandSource.ConsumeSkill(slot);
    }

    private static void ShakeOnAttackRelease(AttackReleaseEffectData release)
    {
        AttackCameraShakeData shake = release.shake;
        if (!shake.enabled || shake.amplitude <= 0f || shake.duration <= 0f) return;

        App.ShakeCamera(
            shake.amplitude,
            shake.duration,
            shake.frequency > 0f ? shake.frequency : 25f);
    }

    // 반응 루프의 반격: 퍼펙트 닷지 후 공격 버튼이 호출한다. 진행 중 동작을 끊고 반격기를 즉시 시전한다.
    // _counterAttack 미할당 시 기본 콤보 1타로 폴백(슬라이스 즉시 테스트용).
    public void TriggerCounter()
    {
        SO_Attack_Data counter = _counterAttack != null
            ? _counterAttack
            : (_attacks != null && _attacks.Length > 0 ? _attacks[0] : null);
        if (counter == null) return;

        CancelAttack();
        ResetCombo();
        StartAttackDataWithEffects(counter);
    }

    private void StartAttack()
    {
        SO_Attack_Data attack = GetData(_comboCount);
        if (attack == null)
        {
            ResetCombo();
            return;
        }
        StartAttackDataWithEffects(attack);
    }

    private void StartAttackDataWithEffects(SO_Attack_Data attack)
    {
        if (attack == null) return;

        StartAttackData(attack);
        ScheduleReleaseEffects(attack.ReleaseEffects, attack.Duration);
        PlayAttackCameraCue(attack, AttackCueTrigger.Release);
    }

    private void ScheduleReleaseEffects(AttackReleaseEffectData release, float attackDuration)
    {
        StopReleaseEffects();

        if (!HasReleaseEffects(release))
            return;

        float delay = Mathf.Max(0f, attackDuration) * Mathf.Clamp01(release.timing);
        if (delay <= 0f)
        {
            PlayReleaseEffects(release);
            return;
        }

        _releaseEffectsCts = new System.Threading.CancellationTokenSource();
        PlayReleaseEffectsDelayed(release, delay, _releaseEffectsCts.Token).Forget();
    }

    private static bool HasReleaseEffects(AttackReleaseEffectData release)
        => (release.shake.enabled && release.shake.amplitude > 0f && release.shake.duration > 0f)
           || (release.slowMo.enabled && release.slowMo.duration > 0f);

    private async UniTaskVoid PlayReleaseEffectsDelayed(AttackReleaseEffectData release, float delay, System.Threading.CancellationToken token)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(delay), DelayType.UnscaledDeltaTime, cancellationToken: token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        PlayReleaseEffects(release);
    }

    private void PlayReleaseEffects(AttackReleaseEffectData release)
    {
        ShakeOnAttackRelease(release);
        if (release.slowMo.enabled)
            TriggerSlowMo(release.slowMo).Forget();
    }

    private void StopReleaseEffects()
    {
        _releaseEffectsCts?.Cancel();
        _releaseEffectsCts?.Dispose();
        _releaseEffectsCts = null;
    }

    private void PollAndDiscardSkillInput()
    {
        if (_skills == null)
            return;

        for (int i = 0; i < _skillCooldowns.Length; i++)
            ConsumeSkill(i);
    }

    private static void PlayAttackCameraCue(SO_Attack_Data attack, AttackCueTrigger trigger)
    {
        if (attack == null) return;

        AttackCameraCueData cue = attack.CameraCue;
        if (!cue.enabled || cue.trigger != trigger) return;

        Game.PlayCameraCutIn(new SkillCutInData
        {
            enabled = true,
            duration = cue.duration > 0f ? cue.duration : Mathf.Max(0.01f, attack.Duration),
            fovOverride = cue.fovOverride,
            distanceOverride = cue.distanceOverride,
            heightDelta = cue.heightDelta,
            yawVelocity = cue.yawVelocity
        });
    }

    private void StartAttackData(SO_Attack_Data attack)
    {
        Vector3 inputAim = _pendingLookDirection;
        bool hasExplicitInput = inputAim.sqrMagnitude > ExplicitLookInputSqrThreshold;
        Vector3 autoAim = hasExplicitInput ? Vector3.zero : FindAutoAimDirection(attack);
        Vector3 lookDir = hasExplicitInput ? inputAim : autoAim;

        if (lookDir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(lookDir);

        if (_drivesCameraFollowAlignment)
            App.AlignThirdPersonCameraToTargetYaw(attack.Duration);

        _pendingLookDirection = Vector3.zero;
        _currentData = attack;
        _comboTimer = 0f;
        _attackTimer = _currentData.Duration;
        _hitboxFired = false;
        _hitboxFireCount = 0;
        _hitCameraCuePlayed = false;
        _nextHitboxElapsed = 0f;
        _skillSequenceAnyHit = false;
        _slamLandingFired = false;
        _attackHitRegistry.Clear();

        _actionHandler?.InterruptJumpArcForAttack();
        InitExtraHitState(_currentData);
        _playerAnimator?.PlayAttack(_currentData.Animation);
        if (_currentData.Lunge.moveType == AttackMoveType.Slam)
            _slamDescending = true;
        _moveController?.StartLunge(transform.forward, _currentData.Lunge);
        if (_currentData.Jump.enabled)
        {
            _moveController?.Jump(_currentData.Jump.height);
        }
        if (ShouldPlayDashVfx(_currentData.Lunge))
        {
            _vfx?.PlayDashStart(transform.forward);
            _attackMoveVfxPlaying = true;
        }

        AttackFeedbackData fb = _currentData.Feedback;
        StopCastVfx();
        if (!string.IsNullOrEmpty(fb.castVfxAddress))
        {
            SpawnCastVfxAsync(fb.castVfxAddress, fb.castVfxOffset, fb.castVfxEuler, fb.castVfxSpace, fb.castVfxScale, _currentData.Duration, fb.castVfxTiming).Forget();
        }
        _vfx?.PlaySwingTrails(fb.swingTrailIds);
        App.PlaySfx(fb.swingSfx, transform.position);

    }

    private void OnAttackEnd()
    {
        bool opensBasicComboWindow = _skillSequence == null
                                     && _attacks != null
                                     && _comboCount + 1 < _attacks.Length;
        _playerAnimator?.ExitAttack(playIdle: !opensBasicComboWindow);
        StopAttackMoveVfx(true);
        StopCastVfx();
        StopReleaseEffects();
        if (_currentData != null)
        {
            // Release 트리거 큐가 남아 있으면 취소 (cancelOnTickMiss 등 조기 종료 시 큐가 계속 재생되는 문제 방지)
            if (_currentData.CameraCue.enabled && _currentData.CameraCue.trigger == AttackCueTrigger.Release)
                Game.CancelCameraCutIn();
            PlayAttackCameraCue(_currentData, AttackCueTrigger.End);
            _vfx?.StopSwingTrails(_currentData.Feedback.swingTrailIds);
        }

        // ?ㅽ궗 ?쒗??吏꾪뻾 以묒씠硫??ㅼ쓬 attack???먮룞 諛쒖궗. ?쒗?ㅺ? ?앸굹硫??쇰컲 肄ㅻ낫 ?곹깭濡?蹂듦?.
        if (_skillSequence != null)
        {
            // AdvanceWithoutHit이면 명중과 무관하게 다음 타로 진행(발사체/장판처럼 비동기로 맞는 스킬용).
            bool requireHit = _skillData == null || !_skillData.AdvanceWithoutHit;
            if (requireHit && !_skillSequenceAnyHit && !CanAdvanceSkillSequenceWithoutHit(_currentData))
            {
                _skillSequence = null;
                _skillData = null;
                _skillSequenceIndex = 0;
                ResetCombo();
                return;
            }

            _skillSequenceIndex++;
            if (_skillSequenceIndex < _skillSequence.Length && _skillSequence[_skillSequenceIndex] != null)
            {
                StartAttackDataWithEffects(_skillSequence[_skillSequenceIndex]);
                return;
            }
            _skillSequence = null;
            _skillData = null;
            _skillSequenceIndex = 0;
            ResetCombo();
            return;
        }

        _comboCount++;
        if (_attacks == null || _comboCount >= _attacks.Length)
        {
            ResetCombo();
            return;
        }
        _comboTimer = _comboWindow;
    }

    private static bool CanAdvanceSkillSequenceWithoutHit(SO_Attack_Data data)
        => data != null
           && data.Damage <= 0f
           && !data.Launch.enabled
           && !data.Down.enabled;

    private void InitExtraHitState(SO_Attack_Data data)
    {
        int count = data.AdditionalHits?.Length ?? 0;
        if (_extraHitFired == null || _extraHitFired.Length < count)
        {
            _extraHitFired = new bool[count];
            _extraNextHitboxElapsed = new float[count];
        }
        for (int i = 0; i < count; i++)
        {
            _extraHitFired[i] = false;
            _extraNextHitboxElapsed[i] = 0f;
        }
    }

    private void ResetCombo()
    {
        _comboCount = 0;
        _comboTimer = 0f;
        _nextQueued = false;
        _pendingLookDirection = Vector3.zero;
        _hitboxFired = false;
        _hitboxFireCount = 0;
        _nextHitboxElapsed = 0f;
        _skillSequenceAnyHit = false;
        _hitCameraCuePlayed = false;
        _slamLandingFired = false;
        _slamDescending = false;
        StopReleaseEffects();
        if (_extraHitFired != null)
            for (int i = 0; i < _extraHitFired.Length; i++)
            {
                _extraHitFired[i] = false;
                _extraNextHitboxElapsed[i] = 0f;
            }
        _attackHitRegistry.Clear();
        StopAttackMoveVfx(false);
        _vfx?.StopAllSwingTrails();
        _playerAnimator?.ReleaseLocomotion();
    }

    public void CancelAttack()
    {
        _attackTimer = 0f;
        _moveController?.StopLunge();
        _slamDescending = false;
        _playerAnimator?.ExitAttack();
        StopCastVfx();
        StopReleaseEffects();
        if (_currentData != null)
        {
            if (_currentData.CameraCue.enabled)
                Game.CancelCameraCutIn();
            _vfx?.StopSwingTrails(_currentData.Feedback.swingTrailIds);
        }
        _skillSequence = null;
        _skillData = null;
        _skillSequenceIndex = 0;
        ResetCombo();
    }

    private void TickAttackHitbox(SO_Attack_Data data, float deltaTime)
    {
        if (data.Lunge.moveType == AttackMoveType.Slam)
        {
            if (_slamDescending && _moveController != null && !_moveController.IsGrounded)
                _vertical?.SlamMove(data.Lunge.slamDescentSpeed, deltaTime);

            if (!_slamLandingFired && (_moveController == null || _moveController.IsGrounded))
            {
                _slamLandingFired = true;
                _slamDescending = false;
                if (ShouldFireMeleeHitbox(data)) FireHitbox(data);
                TrySpawnRangedDelivery(data, includeField: true);
            }
            return;
        }

        AttackHitboxData hitbox = data.Hitbox;
        AttackRepeatData repeat = data.Repeat;
        float elapsed = data.Duration - Mathf.Max(0f, _attackTimer);
        float startElapsed = data.Duration * hitbox.timing;
        if (!_hitboxFired)
        {
            if (elapsed < startElapsed)
                return;

            _hitboxFired = true;
            _hitboxFireCount = 1;
            _nextHitboxElapsed = elapsed + Mathf.Max(0.01f, repeat.interval);
            if (ShouldFireMeleeHitbox(data)) FireHitbox(data);
            TrySpawnRangedDelivery(data, includeField: true);
            return;
        }

        if (!repeat.enabled)
        {
            TickExtraHitboxes(data, elapsed);
            return;
        }

        // repeat.maxCount > 0이면 첫 발동 포함 그 횟수만큼만 발동. 0=무제한(duration 동안).
        bool limited = repeat.maxCount > 0;
        bool fireMelee = ShouldFireMeleeHitbox(data);
        float repeatInterval = Mathf.Max(0.01f, repeat.interval);
        while (_nextHitboxElapsed <= elapsed)
        {
            if (limited && _hitboxFireCount >= repeat.maxCount)
                break;

            bool hit = fireMelee && FireHitbox(data);
            TrySpawnRangedDelivery(data, includeField: false); // 연사: 발사체만 재발사, 장판은 첫 발동 1회
            _hitboxFireCount++;
            _nextHitboxElapsed += repeatInterval;
            if (fireMelee && !hit && repeat.cancelOnMiss)
            {
                _attackTimer = 0f;
                break;
            }
        }

        TickExtraHitboxes(data, elapsed);
    }

    private void TickExtraHitboxes(SO_Attack_Data data, float elapsed)
    {
        AttackExtraHit[] extras = data.AdditionalHits;
        if (extras == null || extras.Length == 0) return;

        float scaledBaseDamage = CombatFormula.ScaleAttackDamage(_attackPower, data.Damage);

        for (int i = 0; i < extras.Length; i++)
        {
            AttackExtraHit extra = extras[i];
            AttackRepeatData repeat = extra.repeat;
            float startElapsed = data.Duration * extra.hitbox.timing;

            if (!_extraHitFired[i])
            {
                if (elapsed < startElapsed) continue;
                _extraHitFired[i] = true;
                _extraNextHitboxElapsed[i] = elapsed + Mathf.Max(0.01f, repeat.interval);
                FireExtraHit(data, extra, i, scaledBaseDamage);
                continue;
            }

            if (!repeat.enabled) continue;

            float repeatInterval = Mathf.Max(0.01f, repeat.interval);
            while (_extraNextHitboxElapsed[i] <= elapsed)
            {
                bool hit = FireExtraHit(data, extra, i, scaledBaseDamage);
                _extraNextHitboxElapsed[i] += repeatInterval;
                if (!hit && repeat.cancelOnMiss)
                {
                    _attackTimer = 0f;
                    break;
                }
            }
        }
    }

    private bool FireExtraHit(SO_Attack_Data data, AttackExtraHit extra, int extraIndex, float scaledBaseDamage)
    {
        float finalDamage = CombatFormula.ScaleAttackDamage(_attackPower, extra.hitResult.damage);

        string timingVfx = data.Feedback.timingVfxAddress;
        if (!string.IsNullOrEmpty(timingVfx))
        {
            Vector3 center = AttackShapeUtility.GetQueryCenter(transform.position, transform.forward, extra.hitbox, extra.shape);
            center += transform.rotation * data.Feedback.timingVfxOffset;
            float vfxDuration = extra.repeat.enabled ? extra.repeat.interval : 0f;
            CombatFeedback.SpawnVfxAtPosition(timingVfx, center, destroyCancellationToken, vfxDuration);
        }

        bool didHit = _emitter.Emit(
            transform.position, transform.forward, extra.hitbox, extra.shape,
            AttackHitInfo.FromExtra(data, extra), extra.hitResult.hitType, finalDamage,
            AttackerFaction, ResolveAttackerEntity(),
            _attackHitRegistry, extraIndex + 10, extra.repeat.hitSameTargetOnce, data);

        _skillSequenceAnyHit |= didHit;
        if (!didHit) return false;

        if (!_hitCameraCuePlayed)
        {
            _hitCameraCuePlayed = true;
            PlayAttackCameraCue(data, AttackCueTrigger.Hit);
        }

        CombatOnHit.ApplyAttackerGains(data, finalDamage, _actionHandler, _playerStats != null ? _playerStats.GaugeGainPerDamage : 0f);
        CombatOnHit.TriggerHitstop(data.HitEffects.hitstop, destroyCancellationToken).Forget();
        return true;
    }

    private bool FireHitbox(SO_Attack_Data data)
    {
        float finalDamage = CombatFormula.ScaleAttackDamage(_attackPower, data.Damage);

        string timingVfx = data.Feedback.timingVfxAddress;
        if (!string.IsNullOrEmpty(timingVfx))
        {
            Vector3 center = AttackShapeUtility.GetQueryCenter(transform.position, transform.forward, data.Hitbox, data.Shape);
            center += transform.rotation * data.Feedback.timingVfxOffset;
            float timingVfxDuration = data.Repeat.enabled ? data.Repeat.interval : 0f;
            CombatFeedback.SpawnVfxAtPosition(timingVfx, center, destroyCancellationToken, timingVfxDuration);
        }

        bool didHit = _emitter.Emit(
            transform.position, transform.forward, data.Hitbox, data.Shape,
            AttackHitInfo.FromMain(data), data.HitType, finalDamage,
            AttackerFaction, ResolveAttackerEntity(),
            _attackHitRegistry, 1, data.Repeat.hitSameTargetOnce, data);

        _skillSequenceAnyHit |= didHit;
        if (!didHit) return false;

        if (!_hitCameraCuePlayed)
        {
            _hitCameraCuePlayed = true;
            PlayAttackCameraCue(data, AttackCueTrigger.Hit);
        }

        CombatOnHit.ApplyAttackerGains(data, finalDamage, _actionHandler, _playerStats != null ? _playerStats.GaugeGainPerDamage : 0f);

        if (data.Lunge.stopOnHit)
        {
            _moveController?.StopLunge();
            StopAttackMoveVfx(true);
        }

        CombatOnHit.TriggerHitstop(data.HitEffects.hitstop, destroyCancellationToken).Forget();
        return true;
    }

    public void UpdateLookDirection(Vector3 worldInput)
    {
        _pendingLookDirection = worldInput;
    }

    // 발사체/장판이 그 공격의 전달 수단이면 근접 메인 hitbox는 스킵한다(Shape·damage를 공유하므로).
    // MeleeAlongsideDelivery가 켜져 있으면 근접도 함께 발동(검 휘두르며 충격파 등).
    private static bool ShouldFireMeleeHitbox(SO_Attack_Data data)
    {
        bool hasDelivery = data.Projectile.enabled || data.Field.enabled;
        return !hasDelivery || data.MeleeAlongsideDelivery;
    }

    // 공격자(이 캐릭터)의 ECS 엔티티. 잡몹이 강제 어그로 대상으로 매칭하는 CharacterNavTarget 엔티티다.
    private Entity ResolveAttackerEntity()
    {
        if (_ecsBridge == null) _ecsBridge = GetComponent<Character_EcsBridge>();
        return _ecsBridge != null ? _ecsBridge.CharacterEntity : Entity.Null;
    }

    // 발사체/장판 발사. includeField=false면 장판은 건너뛰고 발사체만 쏜다.
    // repeat 틱마다 호출되면 발사체는 매 틱 재발사(=연사)되고, 장판은 첫 발동(includeField=true) 1회만 깔린다.
    // 데미지는 공격력으로 스케일한 값을 스냅샷으로 넘긴다(스폰 후 공격자 상태와 무관하게 일관).
    private void TrySpawnRangedDelivery(SO_Attack_Data data, bool includeField)
    {
        AttackProjectileData proj = data.Projectile;
        AttackFieldData field = data.Field;
        bool wantProjectile = proj.enabled && !string.IsNullOrEmpty(proj.prefabAddress);
        bool wantField = includeField && field.enabled && !string.IsNullOrEmpty(field.prefabAddress);
        if (!wantProjectile && !wantField) return;

        float finalDamage = CombatFormula.ScaleAttackDamage(_attackPower, data.Damage);
        RangedOwner owner = new RangedOwner(
            AttackerFaction,
            ResolveAttackerEntity(),
            _actionHandler,
            _playerStats != null ? _playerStats.GaugeGainPerDamage : 0f);

        // ProjectileImpact 장판은 발사체가 도착 시 자체 스폰하므로 컨트롤러는 직접 깔지 않는다.
        bool fieldByProjectile = field.enabled && field.origin == FieldOrigin.ProjectileImpact;

        if (wantProjectile)
            SpawnProjectiles(data, finalDamage, owner, fieldByProjectile);
        if (wantField && !fieldByProjectile)
            SpawnFieldAsync(data, finalDamage, owner).Forget();
    }

    // 멀티샷 발사. count개를 전방 기준 spreadAngle 부채꼴로 균등 분산해 동시에 쏜다.
    private void SpawnProjectiles(SO_Attack_Data data, float finalDamage, RangedOwner owner, bool spawnFieldOnImpact)
    {
        AttackProjectileData proj = data.Projectile;
        int count = Mathf.Max(1, proj.count);
        Vector3 forward = transform.forward;
        Vector3 spawnPos = transform.position + transform.rotation * proj.spawnOffset;

        if (count == 1)
        {
            SpawnOneProjectileAsync(data, finalDamage, owner, spawnFieldOnImpact, spawnPos, forward).Forget();
            return;
        }

        float spread = proj.spreadAngle;
        float step, start;
        if (spread >= 360f) { step = 360f / count; start = 0f; }       // 전방위 균등(끝 겹침 방지)
        else { step = spread / (count - 1); start = -spread * 0.5f; }   // 부채꼴 균등

        for (int i = 0; i < count; i++)
        {
            Vector3 dir = Quaternion.AngleAxis(start + step * i, Vector3.up) * forward;
            SpawnOneProjectileAsync(data, finalDamage, owner, spawnFieldOnImpact, spawnPos, dir).Forget();
        }
    }

    private async UniTaskVoid SpawnOneProjectileAsync(SO_Attack_Data data, float finalDamage, RangedOwner owner, bool spawnFieldOnImpact, Vector3 spawnPos, Vector3 direction)
    {
        Quaternion rot = Quaternion.LookRotation(direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward);

        Projectile_Hitbox projectile = await App.SpawnAsync<Projectile_Hitbox>(data.Projectile.prefabAddress, token: destroyCancellationToken);
        if (projectile == null) return;
        projectile.transform.SetPositionAndRotation(spawnPos, rot);
        projectile.Launch(data, finalDamage, owner, direction, spawnFieldOnImpact);
    }

    private async UniTaskVoid SpawnFieldAsync(SO_Attack_Data data, float finalDamage, RangedOwner owner)
    {
        AttackFieldData field = data.Field;
        Vector3 forward = transform.forward;
        Vector3 spawnPos;
        Transform follow = null;
        if (field.origin == FieldOrigin.AimTarget)
        {
            spawnPos = ResolveAimTargetPosition(owner.Faction, Mathf.Max(0f, field.forwardOffset));
        }
        else // ForwardOffset
        {
            spawnPos = transform.position + forward * field.forwardOffset;
            follow = field.followAttacker ? transform : null;
        }

        Field_Hitbox instance = await App.SpawnAsync<Field_Hitbox>(field.prefabAddress, token: destroyCancellationToken);
        if (instance == null) return;
        instance.transform.position = spawnPos;
        instance.Activate(data, finalDamage, owner, forward, follow);
    }

    private SO_Attack_Data GetData(int index)
        => _attacks != null && _attacks.Length > 0
            ? _attacks[Mathf.Clamp(index, 0, _attacks.Length - 1)]
            : null;


    private async Cysharp.Threading.Tasks.UniTaskVoid SpawnCastVfxAsync(string address, Vector3 offset, Vector3 euler, CastVfxSpace space, Vector3 scale, float duration, float timing)
    {
        _castVfxSpawnCts?.Cancel();
        _castVfxSpawnCts?.Dispose();
        _castVfxSpawnCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);

        System.Threading.CancellationToken token = _castVfxSpawnCts.Token;
        try
        {
            float delay = Mathf.Max(0f, duration) * Mathf.Clamp01(timing);
            if (delay > 0f)
                await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: token);

            var vfx = await App.SpawnAsync<AutoDespawn>(address, token: token);
            if (vfx == null || token.IsCancellationRequested) return;
            Vector3 resolvedScale = ResolveCastVfxScale(scale);
            if (space == CastVfxSpace.Actor)
            {
                vfx.transform.SetParent(transform, false);
                vfx.transform.localPosition = offset;
                vfx.transform.localRotation = Quaternion.Euler(euler);
                vfx.transform.localScale = resolvedScale;
            }
            else
            {
                vfx.transform.SetParent(null, true);
                vfx.transform.position = transform.position + transform.rotation * offset;
                vfx.transform.rotation = transform.rotation * Quaternion.Euler(euler);
                vfx.transform.localScale = resolvedScale;
            }
            vfx.Restart();
            _castVfxInstance = vfx;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static Vector3 ResolveCastVfxScale(Vector3 scale)
        => scale.sqrMagnitude > 0f ? scale : Vector3.one;

    private void StopCastVfx()
    {
        _castVfxSpawnCts?.Cancel();
        _castVfxSpawnCts?.Dispose();
        _castVfxSpawnCts = null;

        if (_castVfxInstance == null) return;
        AutoDespawn instance = _castVfxInstance;
        _castVfxInstance = null;
        if (instance == null) return;
        instance.transform.SetParent(null, true);
        if (instance.gameObject.activeInHierarchy)
            App.Despawn(instance.gameObject);
    }

    private void StopAttackMoveVfx(bool playEnd)
    {
        if (!_attackMoveVfxPlaying)
            return;

        _attackMoveVfxPlaying = false;
        if (playEnd)
            _vfx?.PlayDashEnd(transform.forward);
        else
            _vfx?.StopDash();
    }

    private static bool ShouldPlayDashVfx(AttackLungeData lunge)
        => lunge.moveType == AttackMoveType.Dash || lunge.moveType == AttackMoveType.RushTrack;

    private async UniTaskVoid TriggerSlowMo(AttackSlowMoData slowMo)
    {
        if (slowMo.duration <= 0f) return;

        Main.Loop.SetGameSpeed(Mathf.Clamp01(slowMo.timeScale));
        await UniTask.Delay(
            TimeSpan.FromSeconds(slowMo.duration),
            ignoreTimeScale: true,
            cancellationToken: destroyCancellationToken);
        if (Main.Loop != null)
            Main.Loop.SetGameSpeed(1f);
    }

    private void OnDestroy()
    {
        _castVfxSpawnCts?.Cancel();
        _castVfxSpawnCts?.Dispose();
        StopReleaseEffects();
        _emitter.Dispose();
        if (_cachedWorld != null && _cachedWorld.IsCreated)
            _autoAimQuery.Dispose();
    }

}
