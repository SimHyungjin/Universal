using System;
using Cysharp.Threading.Tasks;
using MapNav.Ecs;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

[DisallowMultipleComponent]
public class Character_AttackController : LoopMonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool drawAttackGizmosAlways;
    [SerializeField] private int gizmoAttackIndex;
    [SerializeField] private Color attackGizmoColor = new Color(1f, 0.2f, 0.2f, 0.35f);

    public int  ComboCount  => _comboCount;
    public bool IsAttacking => _attackTimer > 0f;
    public bool IsInCombo   => _attackTimer > 0f || _comboTimer > 0f;
    public bool IsSkillSequenceActive => _skillSequence != null;
    public bool SuspendsAtApex => IsAttacking && _currentData != null && _currentData.Jump.suspendAtApex
                                  && _currentData.Lunge.moveType != AttackMoveType.Slam;
    public bool IsSlamDescending => _slamDescending && _currentData != null && _currentData.Lunge.moveType == AttackMoveType.Slam;
    public float SlamDescentSpeed => _currentData != null ? _currentData.Lunge.slamDescentSpeed : 0f;
    public bool BlocksMovement => _attackTimer > 0f || (_comboTimer > 0f && _lockMovementDuringComboWindow);
    public bool IsSuperArmoredAgainst(float superArmorBreak)
        => IsAttacking && _currentData != null && _currentData.SuperArmor > superArmorBreak;

    private Character_Animator       _playerAnimator;
    private Character_MoveController _moveController;
    private Character_Vfx            _vfx;
    private Character_ActionHandler  _actionHandler;
    private SO_Character_Stats      _playerStats;
    private float                  _attackPower;
    private SO_Attack_Data[] _attacks;
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
    private readonly Collider[] _autoAimOverlapBuffer = new Collider[128];
    private readonly AttackHitRegistry _attackHitRegistry = new();

    // 근접·발사체·장판이 공유하는 히트 판정 구현(GameObject + ECS).
    private readonly AttackHitEmitter _emitter = new();
    private Character_EcsBridge _ecsBridge;
    private Character_CommandSource _commandSource;
    private bool _drivesCameraFollowAlignment;

    private EntityManager _em;
    private EntityQuery   _autoAimQuery;
    private World         _cachedWorld;

    private void Awake()
    {
        _playerAnimator = GetComponent<Character_Animator>();
        _moveController = GetComponent<Character_MoveController>();
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
        {
            _slamDescending = true;
            _moveController?.StartLunge(transform.forward, _currentData.Lunge);
        }
        else
        {
            _moveController?.StartLunge(transform.forward, _currentData.Lunge);
        }
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
            SpawnCastVfxAsync(fb.castVfxAddress, fb.castVfxOffset, fb.castVfxEuler, _currentData.Duration, fb.castVfxTiming).Forget();
        }
        _vfx?.PlaySwingTrails(fb.swingTrailIds);
        App.PlaySfx(fb.swingSfx, transform.position);

    }

    private void OnAttackEnd()
    {
        _playerAnimator?.ExitAttack();
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
                _moveController.MoveDown(data.Lunge.slamDescentSpeed, deltaTime);

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

    private Vector3 FindAutoAimDirection(SO_Attack_Data data)
    {
        float range = data.Hitbox.offset + AttackShapeUtility.GetPlanarReach(data.Shape);
        Vector3 best = Vector3.zero;
        float bestDist = range * range;
        Vector3 myPos = transform.position;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        int hitCount = Physics.OverlapSphereNonAlloc(myPos, range, _autoAimOverlapBuffer);
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _autoAimOverlapBuffer[i];
            if (!col.TryGetComponent(out IHitTarget target) || !target.IsHittable) continue;
            if (!IsHostileHitTarget(col)) continue;
            Vector3 diff = col.ClosestPoint(myPos) - myPos;
            diff.y = 0f;
            if (diff.sqrMagnitude < 0.0001f) continue;
            if (Vector3.Dot(diff.normalized, forward) < -0.5f) continue;
            float dist = diff.sqrMagnitude;
            if (dist >= bestDist) continue;
            bestDist = dist;
            best = diff.normalized;
        }

        if (EnsureAutoAimQuery())
        {
            NativeArray<LocalTransform> transforms = _autoAimQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            NativeArray<NavAgentDeath> deaths = _autoAimQuery.ToComponentDataArray<NavAgentDeath>(Allocator.Temp);
            NativeArray<NavAgentFaction> factions = _autoAimQuery.ToComponentDataArray<NavAgentFaction>(Allocator.Temp);
            NativeArray<NavAgentSettings> settings = _autoAimQuery.ToComponentDataArray<NavAgentSettings>(Allocator.Temp);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (deaths[i].Dying != 0) continue;
                if (factions[i].Faction == AttackerFaction) continue;
                var f = transforms[i].Position;
                float targetRadius = Mathf.Max(0f, settings[i].AgentRadius);
                Vector3 pos = new Vector3(f.x, f.y, f.z);
                Vector3 diff = pos - myPos;
                diff.y = 0f;
                if (diff.sqrMagnitude < 0.0001f) continue;
                if (Vector3.Dot(diff.normalized, forward) < -0.5f) continue;
                float distToBody = Mathf.Max(0f, diff.magnitude - targetRadius);
                float dist = distToBody * distToBody;
                if (dist >= bestDist) continue;
                bestDist = dist;
                best = diff.normalized;
            }
            transforms.Dispose();
            deaths.Dispose();
            factions.Dispose();
            settings.Dispose();
        }

        return best;
    }

    private bool EnsureAutoAimQuery()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;
        if (world == _cachedWorld) return true;

        if (_cachedWorld != null && _cachedWorld.IsCreated)
            _autoAimQuery.Dispose();

        _cachedWorld = world;
        _em = world.EntityManager;
        _autoAimQuery = _em.CreateEntityQuery(
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<NavAgentKnockback>(),
            ComponentType.ReadOnly<NavAgentDeath>(),
            ComponentType.ReadOnly<NavAgentFaction>(),
            ComponentType.ReadOnly<NavAgentSettings>());
        return true;
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

    // 번개형 장판의 타겟 위치. 전방(조준 반영) 사거리 내 최근접 적 발밑을 우선, 없으면 전방 끝점.
    private Vector3 ResolveAimTargetPosition(NavFaction faction, float maxRange)
    {
        Vector3 myPos = transform.position;
        float searchRange = maxRange > 0f ? maxRange : 9999f;
        float bestDistSq = float.MaxValue;
        Vector3 best = Vector3.zero;
        bool found = false;

        // GameObject 적 (장수·파괴물 등)
        int hitCount = Physics.OverlapSphereNonAlloc(myPos, searchRange, _autoAimOverlapBuffer);
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _autoAimOverlapBuffer[i];
            if (!col.TryGetComponent(out IHitTarget target) || !target.IsHittable) continue;
            if (!IsHostileHitTarget(col)) continue;
            Vector3 p = col.transform.position;
            Vector3 diff = p - myPos; diff.y = 0f;
            float d = diff.sqrMagnitude;
            if (d >= bestDistSq) continue;
            bestDistSq = d; best = p; found = true;
        }

        // ECS 잡몹
        if (EnsureAutoAimQuery())
        {
            NativeArray<LocalTransform> transforms = _autoAimQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            NativeArray<NavAgentDeath> deaths = _autoAimQuery.ToComponentDataArray<NavAgentDeath>(Allocator.Temp);
            NativeArray<NavAgentFaction> factions = _autoAimQuery.ToComponentDataArray<NavAgentFaction>(Allocator.Temp);
            float rangeSq = searchRange * searchRange;
            for (int i = 0; i < transforms.Length; i++)
            {
                if (deaths[i].Dying != 0) continue;
                if (factions[i].Faction == faction) continue;
                var f = transforms[i].Position;
                Vector3 pos = new Vector3(f.x, f.y, f.z);
                Vector3 diff = pos - myPos; diff.y = 0f;
                float d = diff.sqrMagnitude;
                if (d > rangeSq || d >= bestDistSq) continue;
                bestDistSq = d; best = pos; found = true;
            }
            transforms.Dispose();
            deaths.Dispose();
            factions.Dispose();
        }

        if (found) return best;

        Vector3 dir = transform.forward; dir.y = 0f;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        return myPos + dir * (maxRange > 0f ? maxRange : 0f);
    }

    private SO_Attack_Data GetData(int index)
        => _attacks != null && _attacks.Length > 0
            ? _attacks[Mathf.Clamp(index, 0, _attacks.Length - 1)]
            : null;


    private async Cysharp.Threading.Tasks.UniTaskVoid SpawnCastVfxAsync(string address, Vector3 offset, Vector3 euler, float duration, float timing)
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
            vfx.transform.position = transform.position + transform.rotation * offset;
            vfx.transform.rotation = transform.rotation * Quaternion.Euler(euler);
            _castVfxInstance = vfx;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void StopCastVfx()
    {
        _castVfxSpawnCts?.Cancel();
        _castVfxSpawnCts?.Dispose();
        _castVfxSpawnCts = null;

        if (_castVfxInstance == null) return;
        AutoDespawn instance = _castVfxInstance;
        _castVfxInstance = null;
        if (instance != null && instance.gameObject.activeInHierarchy)
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

    private void OnDrawGizmosSelected()
    {
        SO_Attack_Data gizmoData = Application.isPlaying && _currentData != null && IsAttacking
            ? _currentData
            : drawAttackGizmosAlways
                ? GetGizmoData()
                : null;
        if (gizmoData == null) return;

        AttackHitboxData hitbox = gizmoData.Hitbox;
        Color solid = attackGizmoColor;
        solid.a = Mathf.Clamp01(solid.a);
        Gizmos.color = solid;
        DrawAttackShapeGizmo(transform.position, transform.forward, hitbox, gizmoData.Shape, true);
        Color wire = solid;
        wire.a = 0.9f;
        Gizmos.color = wire;
        DrawAttackShapeGizmo(transform.position, transform.forward, hitbox, gizmoData.Shape, false);
    }

    private SO_Attack_Data GetGizmoData()
    {
        if (_attacks == null || _attacks.Length == 0)
            return null;

        return _attacks[Mathf.Clamp(gizmoAttackIndex, 0, _attacks.Length - 1)];
    }

    private static void DrawAttackShapeGizmo(Vector3 origin, Vector3 forward, AttackHitboxData hitbox, AttackShapeData shape, bool solid)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        switch (shape.type)
        {
            case AttackShape.Cone:
                DrawConeGizmo(origin, forward, hitbox, shape, solid);
                break;
            case AttackShape.Box:
                DrawBoxGizmo(origin, forward, hitbox, shape, solid);
                break;
            default:
                Vector3 center = origin + forward * hitbox.offset + Vector3.up * hitbox.yOffset;
                DrawCylinderGizmo(center, Mathf.Max(0f, shape.radius), hitbox.verticalTolerance, solid);
                break;
        }
    }

    private static void DrawConeGizmo(Vector3 origin, Vector3 forward, AttackHitboxData hitbox, AttackShapeData shape, bool solid)
    {
        Vector3 apex = origin + forward * hitbox.offset + Vector3.up * hitbox.yOffset;
        float length = Mathf.Max(shape.radius, shape.length);
        float halfAngle = Mathf.Clamp(shape.angle, 1f, 360f) * 0.5f;
        float verticalTolerance = Mathf.Max(0f, hitbox.verticalTolerance);
        int segments = 24;

        if (solid)
        {
#if UNITY_EDITOR
            Vector3 solidUp = Vector3.up * verticalTolerance;
            Vector3 fromDir = Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward;
            UnityEditor.Handles.color = Gizmos.color;
            UnityEditor.Handles.DrawSolidArc(apex + solidUp, Vector3.up, fromDir, shape.angle, length);
            UnityEditor.Handles.DrawSolidArc(apex - solidUp, Vector3.up, fromDir, shape.angle, length);
#endif
            return;
        }

        Vector3 up = Vector3.up * verticalTolerance;
        DrawConeSlice(apex + up, forward, length, halfAngle, segments);
        DrawConeSlice(apex - up, forward, length, halfAngle, segments);

        Vector3 leftTop = apex + up + Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward * length;
        Vector3 rightTop = apex + up + Quaternion.AngleAxis(halfAngle, Vector3.up) * forward * length;
        Vector3 leftBottom = leftTop - up * 2f;
        Vector3 rightBottom = rightTop - up * 2f;
        Gizmos.DrawLine(apex + up, apex - up);
        Gizmos.DrawLine(leftTop, leftBottom);
        Gizmos.DrawLine(rightTop, rightBottom);
    }

    private static void DrawConeSlice(Vector3 apex, Vector3 forward, float length, float halfAngle, int segments)
    {
        Vector3 previous = apex + Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward * length;
        Gizmos.DrawLine(apex, previous);
        for (int i = 1; i <= segments; i++)
        {
            float t = Mathf.Lerp(-halfAngle, halfAngle, i / (float)segments);
            Vector3 next = apex + Quaternion.AngleAxis(t, Vector3.up) * forward * length;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
        Gizmos.DrawLine(apex, previous);
    }

    private static void DrawBoxGizmo(Vector3 origin, Vector3 forward, AttackHitboxData hitbox, AttackShapeData shape, bool solid)
    {
        float length = shape.length;
        float width  = shape.width;
        float height = Mathf.Max(0.05f, Mathf.Max(0f, hitbox.verticalTolerance) * 2f);
        Vector3 center = origin + forward * (hitbox.offset + length * 0.5f) + Vector3.up * hitbox.yOffset;
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
        Vector3 size = new Vector3(width, height, length);
        if (solid) Gizmos.DrawCube(Vector3.zero, size);
        else Gizmos.DrawWireCube(Vector3.zero, size);
        Gizmos.matrix = previousMatrix;
    }

    private static void DrawCylinderGizmo(Vector3 center, float radius, float verticalTolerance, bool solid)
    {
        float halfHeight = Mathf.Max(0f, verticalTolerance);
        if (solid)
        {
#if UNITY_EDITOR
            Vector3 solidUp = Vector3.up * halfHeight;
            UnityEditor.Handles.color = Gizmos.color;
            UnityEditor.Handles.DrawSolidDisc(center + solidUp, Vector3.up, radius);
            UnityEditor.Handles.DrawSolidDisc(center,           Vector3.up, radius);
            UnityEditor.Handles.DrawSolidDisc(center - solidUp, Vector3.up, radius);
#endif
            return;
        }

        Vector3 up = Vector3.up * halfHeight;
        DrawCircle(center, radius);
        DrawCircle(center + up, radius);
        DrawCircle(center - up, radius);

        Vector3[] anchors =
        {
            Vector3.forward,
            Vector3.back,
            Vector3.right,
            Vector3.left
        };

        for (int i = 0; i < anchors.Length; i++)
        {
            Vector3 offset = anchors[i] * radius;
            Gizmos.DrawLine(center + up + offset, center - up + offset);
        }
    }

    private static void DrawCircle(Vector3 center, float radius)
    {
        const int segments = 32;
        if (radius <= 0f)
            return;

        Vector3 previous = center + Vector3.forward * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
