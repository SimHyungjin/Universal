using System;
using Cysharp.Threading.Tasks;
using MapNav.Ecs;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

[DisallowMultipleComponent]
public class Player_Attackcontroller : LoopMonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool drawAttackGizmosAlways;
    [SerializeField] private int gizmoAttackIndex;
    [SerializeField] private Color attackGizmoColor = new Color(1f, 0.2f, 0.2f, 0.35f);

    public int  ComboCount  => _comboCount;
    public bool IsAttacking => _attackTimer > 0f;
    public bool IsInCombo   => _attackTimer > 0f || _comboTimer > 0f;
    public bool SuspendsAtApex => IsAttacking && _currentData != null && _currentData.Jump.suspendAtApex
                                  && _currentData.Lunge.moveType != AttackMoveType.Slam;
    public bool IsSlamDescending => _slamDescending && _currentData != null && _currentData.Lunge.moveType == AttackMoveType.Slam;
    public float SlamDescentSpeed => _currentData != null ? _currentData.Lunge.slamDescentSpeed : 0f;
    public bool BlocksMovement => _attackTimer > 0f || (_comboTimer > 0f && _lockMovementDuringComboWindow);
    public bool IsSuperArmoredAgainst(float superArmorBreak)
        => IsAttacking && _currentData != null && _currentData.SuperArmor > superArmorBreak;

    private Player_Animator       _playerAnimator;
    private Player_Movecontroller _moveController;
    private Player_Vfx            _vfx;
    private Player_ActionHandler  _actionHandler;
    private SO_PlayerStats        _playerStats;
    private SO_AttackData[] _attacks;
    private float _comboWindow = 0.35f;
    private bool _lockMovementDuringComboWindow = true;
    private const float ExplicitLookInputSqrThreshold = 0.04f;
    private int   _comboCount;
    private float _attackTimer;
    private float _comboTimer;
    private bool  _nextQueued;
    private Vector3       _pendingLookDirection;
    private SO_AttackData _currentData;
    private bool          _hitboxFired;
    private float         _nextHitboxElapsed;
    private bool          _attackMoveVfxPlaying;
    private AutoDespawn   _castVfxInstance;
    private System.Threading.CancellationTokenSource _castVfxSpawnCts;
    private bool          _slamLandingFired;
    private bool          _slamDescending;

    // Skills (loadout). 4媛??щ’ ??[[project_skill_loadout]]. ?먯썝 ?쒖뒪?쒖? 誘멸뎄?꾩씠??cost???쇰떒 臾댁떆.
    private SO_SkillData[] _skills;
    private readonly float[] _skillCooldowns = new float[SkillInput.SlotCount];
    private SO_AttackData[] _skillSequence;
    private int             _skillSequenceIndex;
    private bool  _skillSequenceAnyHit;
    private bool _hitCameraCuePlayed;
    private bool[]  _extraHitFired;
    private float[] _extraNextHitboxElapsed;
    private readonly Collider[] _hitboxOverlapBuffer = new Collider[128];
    private readonly Collider[] _autoAimOverlapBuffer = new Collider[128];
    private readonly AttackHitRegistry _attackHitRegistry = new();

    private IHitboxProcessor _hitboxProcessor;

    private EntityManager _em;
    private EntityQuery   _autoAimQuery;
    private World         _cachedWorld;

    private void Awake()
    {
        _playerAnimator = GetComponent<Player_Animator>();
        _moveController = GetComponent<Player_Movecontroller>();
        _vfx = GetComponent<Player_Vfx>();
        _actionHandler = GetComponent<Player_ActionHandler>();
        _hitboxProcessor = GetComponent<IHitboxProcessor>();
    }

    public void SetPlayerStats(SO_PlayerStats stats)
    {
        _playerStats = stats;
    }

    public void SetBasicAttackCombo(SO_AttackComboData combo)
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

    public void SetSkills(SO_SkillData[] skills)
    {
        _skills = skills;
        for (int i = 0; i < _skillCooldowns.Length; i++)
            _skillCooldowns[i] = 0f;
    }

    public SO_SkillData GetSkillData(int slot)
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
        SO_SkillData skill = _skills[slot];
        if (skill == null) return false;
        if (_skillCooldowns[slot] > 0f) return false;

        SO_AttackData[] sequence = skill.AttackSequence;
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
        _skillSequenceIndex = 0;
        StartAttackDataWithEffects(sequence[0]);
        return true;
    }

    public bool RequestAttack()
    {
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
        PollSkillInput();

        if (_attackTimer > 0f)
        {
            if (!_slamDescending)
                _attackTimer -= gdt;

            TickAttackHitbox(_currentData);

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
            if (!InputProvider.ConsumeSkill(i)) continue;
            if (TryTriggerSkill(i)) return;
        }
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
        SO_AttackData attack = GetData(_comboCount);
        if (attack == null)
        {
            ResetCombo();
            return;
        }
        StartAttackDataWithEffects(attack);
    }

    private void StartAttackDataWithEffects(SO_AttackData attack)
    {
        if (attack == null) return;

        ShakeOnAttackRelease(attack.ReleaseEffects);
        StartAttackData(attack);
        PlayAttackCameraCue(attack, AttackCueTrigger.Release);
    }

    private static void PlayAttackCameraCue(SO_AttackData attack, AttackCueTrigger trigger)
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

    private void StartAttackData(SO_AttackData attack)
    {
        Vector3 inputAim = _pendingLookDirection;
        bool hasExplicitInput = inputAim.sqrMagnitude > ExplicitLookInputSqrThreshold;
        Vector3 autoAim = hasExplicitInput ? Vector3.zero : FindAutoAimDirection(attack);
        Vector3 lookDir = hasExplicitInput ? inputAim : autoAim;

        if (lookDir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(lookDir);

        App.AlignThirdPersonCameraToTargetYaw(attack.Duration);

        _pendingLookDirection = Vector3.zero;
        _currentData = attack;
        _comboTimer = 0f;
        _attackTimer = _currentData.Duration;
        _hitboxFired = false;
        _hitCameraCuePlayed = false;
        _nextHitboxElapsed = 0f;
        _skillSequenceAnyHit = false;
        _slamLandingFired = false;
        _attackHitRegistry.Clear();

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
            _moveController?.Jump(_currentData.Jump.height);
        if (ShouldPlayDashVfx(_currentData.Lunge))
        {
            _vfx?.PlayDashStart(transform.forward);
            _attackMoveVfxPlaying = true;
        }

        AttackFeedbackData fb = _currentData.Feedback;
        StopCastVfx();
        if (!string.IsNullOrEmpty(fb.castVfxAddress))
        {
            Vector3 castPos = transform.position + transform.rotation * fb.castVfxOffset;
            SpawnCastVfxAsync(fb.castVfxAddress, castPos).Forget();
        }
        _vfx?.PlaySwingTrails(fb.swingTrailIds);
        App.PlaySfx(fb.swingSfx, transform.position);

        if (_currentData.SlowMo.enabled)
            TriggerSlowMo(_currentData.SlowMo).Forget();
    }

    private void OnAttackEnd()
    {
        _playerAnimator?.ExitAttack();
        StopAttackMoveVfx(true);
        StopCastVfx();
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
            if (!_skillSequenceAnyHit)
            {
                _skillSequence = null;
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

    private void InitExtraHitState(SO_AttackData data)
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
        _nextHitboxElapsed = 0f;
        _skillSequenceAnyHit = false;
        _hitCameraCuePlayed = false;
        _slamLandingFired = false;
        _slamDescending = false;
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
        _playerAnimator?.ExitAttack();
        StopCastVfx();
        if (_currentData != null)
        {
            if (_currentData.CameraCue.enabled)
                Game.CancelCameraCutIn();
            _vfx?.StopSwingTrails(_currentData.Feedback.swingTrailIds);
        }
        _skillSequence = null;
        _skillSequenceIndex = 0;
        ResetCombo();
    }

    private void TickAttackHitbox(SO_AttackData data)
    {
        if (data.Lunge.moveType == AttackMoveType.Slam)
        {
            if (!_slamLandingFired && (_moveController == null || _moveController.IsGrounded))
            {
                _slamLandingFired = true;
                _slamDescending = false;
                FireHitbox(data);
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
            _nextHitboxElapsed = elapsed + Mathf.Max(0.01f, repeat.interval);
            FireHitbox(data);
            return;
        }

        if (!repeat.enabled)
        {
            TickExtraHitboxes(data, elapsed);
            return;
        }

        float repeatInterval = Mathf.Max(0.01f, repeat.interval);
        while (_nextHitboxElapsed <= elapsed)
        {
            bool hit = FireHitbox(data);
            _nextHitboxElapsed += repeatInterval;
            if (!hit && repeat.cancelOnMiss)
            {
                _attackTimer = 0f;
                break;
            }
        }

        TickExtraHitboxes(data, elapsed);
    }

    private void TickExtraHitboxes(SO_AttackData data, float elapsed)
    {
        AttackExtraHit[] extras = data.AdditionalHits;
        if (extras == null || extras.Length == 0) return;

        float scaledBaseDamage = _playerStats != null
            ? CombatFormula.ScaleAttackDamage(_playerStats.AttackPower, data.Damage)
            : data.Damage;

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

    private bool FireExtraHit(SO_AttackData data, AttackExtraHit extra, int extraIndex, float scaledBaseDamage)
    {
        float finalDamage = _playerStats != null
            ? CombatFormula.ScaleAttackDamage(_playerStats.AttackPower, extra.hitResult.damage)
            : extra.hitResult.damage;
        float suspendDuration = extra.hitResult.launch.suspendDuration;

        string timingVfx = data.Feedback.timingVfxAddress;
        if (!string.IsNullOrEmpty(timingVfx))
        {
            Vector3 center = AttackShapeUtility.GetQueryCenter(transform.position, transform.forward, extra.hitbox, extra.shape);
            center += transform.rotation * data.Feedback.timingVfxOffset;
            float vfxDuration = extra.repeat.enabled ? extra.repeat.interval : 0f;
            CombatFeedback.SpawnVfxAtPosition(timingVfx, center, destroyCancellationToken, vfxDuration);
        }

        bool didHit = FireExtraHitInstance(data, extra, extraIndex, finalDamage);

        if (_hitboxProcessor != null &&
            _hitboxProcessor.ProcessExtra(data, extra, extraIndex, transform, _attackHitRegistry, finalDamage, suspendDuration))
            didHit = true;

        _skillSequenceAnyHit |= didHit;
        if (!didHit) return false;

        if (!_hitCameraCuePlayed)
        {
            _hitCameraCuePlayed = true;
            PlayAttackCameraCue(data, AttackCueTrigger.Hit);
        }

        if (data.LifeSteal.enabled)
        {
            float heal = finalDamage * data.LifeSteal.ratio;
            if (data.LifeSteal.maxPerHit > 0f)
                heal = Mathf.Min(heal, data.LifeSteal.maxPerHit);
            _actionHandler?.Heal(heal);
        }

        _actionHandler?.AddGauge(finalDamage * (_playerStats != null ? _playerStats.GaugeGainPerDamage : 0f));
        TriggerHitstop(data.HitEffects.hitstop).Forget();
        return true;
    }

    private bool FireExtraHitInstance(SO_AttackData data, AttackExtraHit extra, int extraIndex, float finalDamage)
    {
        Vector3 center = AttackShapeUtility.GetQueryCenter(transform.position, transform.forward, extra.hitbox, extra.shape);
        float queryRadius = AttackShapeUtility.GetQueryRadius(extra.hitbox, extra.shape);
        bool didHit = false;

        int hitCount = Physics.OverlapSphereNonAlloc(center, queryRadius, _hitboxOverlapBuffer);
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _hitboxOverlapBuffer[i];
            if (!col.TryGetComponent(out IHitTarget target)) continue;
            Vector3 targetPoint = col.ClosestPoint(center);
            if (!AttackShapeUtility.Contains(transform.position, transform.forward, targetPoint, extra.hitbox, extra.shape))
                continue;
            if (!_attackHitRegistry.TryRegister(col.GetInstanceID(), extraIndex + 10, extra.repeat.hitSameTargetOnce))
                continue;

            target.ReceiveHit(transform.position, transform.forward, data, finalDamage);
            SpawnHitFeedback(data, targetPoint);
            didHit = true;
        }

        return didHit;
    }

    private bool FireHitbox(SO_AttackData data)
    {
        float finalDamage = _playerStats != null
            ? CombatFormula.ScaleAttackDamage(_playerStats.AttackPower, data.Damage)
            : data.Damage;

        string timingVfx = data.Feedback.timingVfxAddress;
        if (!string.IsNullOrEmpty(timingVfx))
        {
            Vector3 center = AttackShapeUtility.GetQueryCenter(transform.position, transform.forward, data.Hitbox, data.Shape);
            center += transform.rotation * data.Feedback.timingVfxOffset;
            float timingVfxDuration = data.Repeat.enabled ? data.Repeat.interval : 0f;
            CombatFeedback.SpawnVfxAtPosition(timingVfx, center, destroyCancellationToken, timingVfxDuration);
        }

        bool didHit = FireHitInstance(data, data.Hitbox, data.Shape, finalDamage);

        float targetSuspendDuration = GetTargetSuspendDuration(data);
        if (_hitboxProcessor != null && _hitboxProcessor.Process(data, transform, _attackHitRegistry, finalDamage, targetSuspendDuration))
            didHit = true;

        _skillSequenceAnyHit |= didHit;
        if (!didHit) return false;

        if (!_hitCameraCuePlayed)
        {
            _hitCameraCuePlayed = true;
            PlayAttackCameraCue(data, AttackCueTrigger.Hit);
        }

        if (data.LifeSteal.enabled)
        {
            float heal = finalDamage * data.LifeSteal.ratio;
            if (data.LifeSteal.maxPerHit > 0f)
                heal = Mathf.Min(heal, data.LifeSteal.maxPerHit);
            _actionHandler?.Heal(heal);
        }

        _actionHandler?.AddGauge(finalDamage * (_playerStats != null ? _playerStats.GaugeGainPerDamage : 0f));

        if (data.Lunge.stopOnHit)
        {
            _moveController?.StopLunge();
            StopAttackMoveVfx(true);
        }

        TriggerHitstop(data.HitEffects.hitstop).Forget();
        return true;
    }

    private static float GetTargetSuspendDuration(SO_AttackData data)
        => data != null ? data.Launch.suspendDuration : 0f;

    private bool FireHitInstance(SO_AttackData data, AttackHitboxData hitbox, AttackShapeData shape, float finalDamage)
    {
        Vector3 center = AttackShapeUtility.GetQueryCenter(transform.position, transform.forward, hitbox, shape);
        float queryRadius = AttackShapeUtility.GetQueryRadius(hitbox, shape);
        bool didHit = false;

        int hitCount = Physics.OverlapSphereNonAlloc(center, queryRadius, _hitboxOverlapBuffer);
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _hitboxOverlapBuffer[i];
            if (!col.TryGetComponent(out IHitTarget target)) continue;
            Vector3 targetPoint = col.ClosestPoint(center);
            if (!AttackShapeUtility.Contains(transform.position, transform.forward, targetPoint, hitbox, shape))
                continue;
            if (!_attackHitRegistry.TryRegister(col.GetInstanceID(), 1, data.Repeat.hitSameTargetOnce))
                continue;

            target.ReceiveHit(transform.position, transform.forward, data, finalDamage);
            SpawnHitFeedback(data, targetPoint);
            didHit = true;
        }

        return didHit;
    }

    public void UpdateLookDirection(Vector3 worldInput)
    {
        _pendingLookDirection = worldInput;
    }

    private Vector3 FindAutoAimDirection(SO_AttackData data)
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
            if (!col.TryGetComponent(out IHitTarget _)) continue;
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
                if (factions[i].Faction != NavFaction.Enemy) continue;
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

    private SO_AttackData GetData(int index)
        => _attacks != null && _attacks.Length > 0
            ? _attacks[Mathf.Clamp(index, 0, _attacks.Length - 1)]
            : null;

    // hit VFX??罹먮┃?곗쓽 媛??諛??믪씠?먯꽌 ?좎빞 ?먯뿰?ㅻ읇?? 諛??꾩튂 湲곗??쇰줈 +0.5m 蹂댁젙.
    private const float HitVfxHeightOffset = 0.5f;

    private void SpawnHitFeedback(SO_AttackData data, Vector3 position)
    {
        position.y += HitVfxHeightOffset;
        CombatFeedback.PlayHitFeedback(data, position, destroyCancellationToken);
    }

    private async Cysharp.Threading.Tasks.UniTaskVoid SpawnCastVfxAsync(string address, Vector3 position)
    {
        _castVfxSpawnCts?.Cancel();
        _castVfxSpawnCts?.Dispose();
        _castVfxSpawnCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);

        var vfx = await App.SpawnAsync<AutoDespawn>(address, token: _castVfxSpawnCts.Token);
        if (vfx == null) return;
        vfx.transform.position = position;
        _castVfxInstance = vfx;
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

    private async UniTaskVoid TriggerHitstop(AttackHitstopData hitstop)
    {
        if (hitstop.duration <= 0f) return;

        Main.Loop.SetGameSpeed(hitstop.timeScale);
        await UniTask.Delay(
            TimeSpan.FromSeconds(hitstop.duration),
            ignoreTimeScale: true,
            cancellationToken: destroyCancellationToken);
        Main.Loop.SetGameSpeed(1f);
    }

    private async UniTaskVoid TriggerSlowMo(AttackSlowMoData slowMo)
    {
        if (slowMo.duration <= 0f) return;

        Main.Loop.SetTimeScales(Mathf.Clamp01(slowMo.worldScale), 1f);
        await UniTask.Delay(
            TimeSpan.FromSeconds(slowMo.duration),
            ignoreTimeScale: true,
            cancellationToken: destroyCancellationToken);
        if (Main.Loop != null)
            Main.Loop.SetTimeScales(1f, 1f);
    }

    private void OnDestroy()
    {
        _castVfxSpawnCts?.Cancel();
        _castVfxSpawnCts?.Dispose();
        if (_cachedWorld != null && _cachedWorld.IsCreated)
            _autoAimQuery.Dispose();
    }

    private void OnDrawGizmosSelected()
    {
        SO_AttackData gizmoData = Application.isPlaying && _currentData != null && IsAttacking
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

    private SO_AttackData GetGizmoData()
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


