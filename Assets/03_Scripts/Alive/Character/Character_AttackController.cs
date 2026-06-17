using System;
using System.Collections.Generic;
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


    public bool IsTelegraphing => _windupActive;
    public bool IsAttacking => _attackTimer > 0f;
    public bool IsInCombo   => _attackTimer > 0f || _comboTimer > 0f;
    public bool IsComboWindowOpen => _attackTimer <= 0f && _comboTimer > 0f;
    public bool IsSkillSequenceActive => _skillSequence != null;
    public bool SuspendsAtApex => IsAttacking && _currentData != null
                                  && _suspendAtApexActive && !_slamDescending;
    public bool IsSlamDescending => _slamDescending;
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
    private bool          _afterimageFeedbackActive;
    private float         _afterimageFeedbackEndElapsed; // motionAfterimages 이벤트 endTime(>0이면 그 elapsed에 정지, 0이면 공격 종료에서)
    // 추적되는 Actor 공간 피드백 VFX. eventIndex로 소속 피드백 이벤트를 알아 endTime에 개별 디스폰한다.
    private readonly List<(int eventIndex, AutoDespawn vfx)> _feedbackVfxInstances = new();
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
    private bool[] _deliveryStarted;
    private bool[] _deliveryEnded;
    private bool[] _deliveryAnyHit;
    private int[] _deliveryFireCount;
    private float[] _deliveryNextFireElapsed;
    private bool[] _movementStarted;
    private bool[] _movementEnded;
    private bool[] _feedbackFired;
    private bool _suspendAtApexActive;
    private readonly AttackHitRegistry _attackHitRegistry = new();

    // 근접·발사체·장판이 공유하는 히트 판정 구현(GameObject + ECS).
    private readonly AttackHitEmitter _emitter = new();
    private Character_EcsBridge _ecsBridge;
    private Character_CommandSource _commandSource;
    private bool _drivesCameraFollowAlignment;

    // 공격 범위 예고(telegraph). 적 시전 시 공격의 windup(0~timing) 구간을 leadTime까지 늘려
    // 그동안 바닥 데칼을 띄운다. 별도 페이즈가 아니라 진짜 공격이라 슈퍼아머가 유지된다. [[project_attack_telegraph]].
    private Character_AttackTelegraph _telegraph;
    private float _windupStretch = 1f;   // >1이면 windup을 그만큼 늘려 재생(애니+타이머)
    private bool  _windupActive;          // windup(느린 예비동작) 진행 중 — 조준 잠금/데칼 표시 구간
    private bool  _pendingTelegraph;          // 다음 StartAttackData에서 예고 데칼을 띄울지(표시 여부 ≠ leadTime)
    private float _pendingTelegraphLeadTime; // 최소 예고 시간. 자연 windup(첫타 startTime)보다 길 때만 windup을 늘림(leadTime 0 가능)
    private Color _pendingTelegraphColor = Color.red;

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
        _telegraph = GetComponent<Character_AttackTelegraph>();
        if (_telegraph == null)
            _telegraph = gameObject.AddComponent<Character_AttackTelegraph>();
    }

    // 카메라 추종 정렬을 이 캐릭터가 구동하는가. 플레이어 빙의 시에만 true(PlayerController.Possess가 주입), AI는 false.
    public void SetDrivesCameraFollowAlignment(bool value) => _drivesCameraFollowAlignment = value;

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

        // 예고: 적(비 로컬플레이어 = 빙의 안 된 AI)이 시전할 때 예고 데칼을 띄운다. leadTime은 "최소 예고 시간"이라
        // 첫타 startTime(자연 windup)보다 길 때만 windup을 그만큼 늘리고, leadTime 0이면 자연 windup 동안 그대로 표시한다.
        // 플레이어(빙의체)는 즉발(손맛 유지). juice 게이트(IsLocalPlayer)의 반대 방향. StartAttackData가 소비.
        AttackTelegraphData tg = skill.Telegraph;
        if (tg.enabled && !PlayerController.IsLocalPlayer(_actionHandler))
        {
            _pendingTelegraph = true;
            _pendingTelegraphLeadTime = tg.leadTime;
            _pendingTelegraphColor = tg.color;
        }

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
            if (!CanQueueCurrentAttack())
                return false;
            _nextQueued = true;
            return true;
        }

        if (_comboCount > 0 && _comboTimer <= 0f)
            ResetCombo();

        _nextQueued = false;
        StartAttack();
        return IsAttacking;
    }

    private bool CanQueueCurrentAttack()
    {
        // 콤보 선입력: 공격이 진행 중이면 다음 입력을 항상 버퍼링한다(공격 종료 후 자동 발사).
        // 구 SO flow.comboQueue 창은 전 에셋이 0..totalDuration로 균일해 변별력이 없어 코드 기본규칙으로 흡수.
        return _currentData != null;
    }

    protected override void OnGameUpdate(float gdt)
    {
        base.OnGameUpdate(gdt);

        TickSkillCooldowns(gdt);
        if (_actionHandler != null && _actionHandler.IsSectorGateTransitioning)
        {
            PollAndDiscardSkillInput();
            if (IsAttacking || IsInCombo || _skillSequence != null || IsTelegraphing)
                CancelAttack();
            return;
        }

        PollSkillInput();

        if (_attackTimer > 0f)
        {
            // windup(예고) 구간이면 느린 dt를 받아 hitbox가 leadTime 시점에 발동하도록 늘린다.
            float dt = TickWindup(gdt);
            if (!_slamDescending)
                _attackTimer -= dt;

            TickMovementEvents(_currentData, dt);
            TickFeedbackEvents(_currentData);
            TickDeliveryEvents(_currentData);

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
    }

    private void PollAndDiscardSkillInput()
    {
        if (_skills == null)
            return;

        for (int i = 0; i < _skillCooldowns.Length; i++)
            ConsumeSkill(i);
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
            App.AlignThirdPersonCameraToTargetYaw(GetAttackDuration(attack));

        _pendingLookDirection = Vector3.zero;
        _currentData = attack;
        _comboTimer = 0f;
        _attackTimer = GetAttackDuration(_currentData);
        _hitCameraCuePlayed = false;
        _skillSequenceAnyHit = false;
        _slamLandingFired = false;
        _suspendAtApexActive = false;
        _attackHitRegistry.Clear();

        _actionHandler?.InterruptJumpArcForAttack();
        InitDeliveryEventState(_currentData);
        InitMovementEventState(_currentData);
        InitFeedbackEventState(_currentData);
        _playerAnimator?.PlayAttack(_currentData.Animation);
        SetupWindup(_currentData);
        // windup(예고)으로 미룬 경우 돌진/대시 VFX는 hitbox 발동 시점(EndWindup)에 시작한다.

        // 스윙 연출(트레일·SFX·cast VFX)은 실제 휘두름에 맞춰야 한다. windup(예고)이 있으면
        // 느린 예비동작 중에 먼저 나오지 않도록 hitbox 발동(EndWindup) 시점으로 미룬다.
        TickFeedbackEvents(_currentData);
    }

    // cast VFX·스윙 트레일·스윙 SFX를 재생한다. windup 뒤에 부를 때(castImmediate)는 이미 hitbox 시점이므로
    // cast VFX의 timing 지연을 0으로 둬 휘두름과 동시에 나오게 한다.
    // 예고가 예약돼 있으면 데칼을 띄운다. 표시 시간 = max(첫타 startTime, leadTime):
    // leadTime이 더 길 때만 windup(0~timing)을 그만큼 늘리고(stretch>1), 아니면 자연 windup 동안 그대로 표시한다(stretch=1, leadTime 0 포함).
    // 애니 재생속도를 낮추고 바닥 데칼을 띄우며, 돌진은 hitbox 발동까지 미룬다.
    private void SetupWindup(SO_Attack_Data attack)
    {
        _windupActive = false;
        _windupStretch = 1f;
        _playerAnimator?.SetAttackSpeedScale(1f);

        bool requested = _pendingTelegraph;
        _pendingTelegraph = false;
        float leadTime = _pendingTelegraphLeadTime;
        _pendingTelegraphLeadTime = 0f;
        if (!requested) return;

        float windup = GetAttackWindupDuration(attack);
        if (windup <= 0.01f) return; // 자연 windup(첫타 startTime)이 없으면 예고할 구간이 없다

        // leadTime은 "최소 예고 시간". windup이 이미 그보다 길면 늘리지 않고(stretch=1) 원래 windup 동안 예고만 띄운다.
        _windupStretch = Mathf.Max(1f, leadTime / windup);
        _windupActive = true;
        if (_windupStretch > 1f)
            _playerAnimator?.SetAttackSpeedScale(1f / _windupStretch);

        _telegraph ??= GetComponent<Character_AttackTelegraph>();
        _telegraph?.Show(attack, _pendingTelegraphColor, leadTime);
    }

    // 돌진/대시처럼 시전자를 이동시키는 lunge인가(Slam은 별도 강하 로직이라 제외).
    // windup 구간이면 dt를 stretch만큼 늦추고 데칼 진행도를 갱신한다.
    // hitbox 발동 시점(timing 도달)에 정상 속도로 복귀하고 미뤘던 돌진을 시작한다.
    private float TickWindup(float gdt)
    {
        if (!_windupActive) return gdt;

        float startElapsed = GetAttackWindupDuration(_currentData);
        float elapsed = GetAttackDuration(_currentData) - Mathf.Max(0f, _attackTimer);
        if (elapsed < startElapsed)
        {
            float progress = startElapsed > 0.0001f ? Mathf.Clamp01(elapsed / startElapsed) : 1f;
            _telegraph?.Tick(progress);
            // 늘릴 때만 느린 dt, 이미 충분히 길면(stretch=1) 정상 속도로 진행하되 예고는 그대로 표시.
            return _windupStretch > 1f ? gdt / _windupStretch : gdt;
        }

        EndWindup();
        return gdt;
    }

    // windup 종료: 정상 속도 복귀 + 데칼 끄기 + 미뤘던 돌진/대시 VFX 시작.
    private void EndWindup()
    {
        if (!_windupActive) return;
        _windupActive = false;
        _windupStretch = 1f;
        _playerAnimator?.SetAttackSpeedScale(1f);
        _telegraph?.Hide();

        // windup 동안 미뤘던 스윙 연출(트레일·SFX·cast VFX)을 실제 휘두름 시점에 시작한다.
    }

    // windup 도중 공격이 취소될 때 정리(속도·데칼·미룬 돌진 상태 원복).
    private void CancelWindup()
    {
        _pendingTelegraph = false;
        _pendingTelegraphLeadTime = 0f;
        if (!_windupActive && _windupStretch <= 1f) return;
        _windupActive = false;
        _windupStretch = 1f;
        _playerAnimator?.SetAttackSpeedScale(1f);
        _telegraph?.Hide();
    }

    private void OnAttackEnd()
    {
        bool opensBasicComboWindow = _skillSequence == null
                                     && _attacks != null
                                     && _comboCount + 1 < _attacks.Length;
        _playerAnimator?.ExitAttack(playIdle: !opensBasicComboWindow);
        StopFeedbackAfterimages();
        StopFeedbackVfx();
        if (_currentData != null)
        {
            // Release 트리거 큐가 남아 있으면 취소 (cancelOnTickMiss 등 조기 종료 시 큐가 계속 재생되는 문제 방지).
            // 컷인은 로컬 플레이어만 재생하므로 취소도 플레이어만 — 적 공격 종료가 플레이어의 활성 컷인을 끊지 않게.
            if (PlayerController.IsLocalPlayer(_actionHandler)
                && AttackTimelineUtility.HasCameraCue(_currentData, AttackCueTrigger.Release))
                Game.CancelCameraCutIn();
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
        => data != null && !HasDamagingOrControlDelivery(data);

    private static bool HasDamagingOrControlDelivery(SO_Attack_Data data)
    {
        AttackDeliveryEvent[] deliveries = data.DeliveryEvents;
        if (deliveries == null) return false;

        for (int i = 0; i < deliveries.Length; i++)
        {
            if (!deliveries[i].enabled) continue;
            AttackHitResultData result = deliveries[i].hitResult;
            if (result.damage > 0f || result.targetLaunch.enabled || result.landingDown.enabled)
                return true;
        }
        return false;
    }

    private static float GetAttackDuration(SO_Attack_Data data)
        => AttackTimelineUtility.GetDuration(data);

    private static float GetAttackWindupDuration(SO_Attack_Data data)
        => AttackTimelineUtility.GetFirstDeliveryStartTime(data);

    private void InitDeliveryEventState(SO_Attack_Data data)
    {
        int count = data != null && data.HasDeliveryEvents ? data.DeliveryEvents.Length : 0;
        if (_deliveryStarted == null || _deliveryStarted.Length < count)
        {
            _deliveryStarted = new bool[count];
            _deliveryEnded = new bool[count];
            _deliveryAnyHit = new bool[count];
            _deliveryFireCount = new int[count];
            _deliveryNextFireElapsed = new float[count];
        }

        for (int i = 0; i < count; i++)
        {
            _deliveryStarted[i] = false;
            _deliveryEnded[i] = false;
            _deliveryAnyHit[i] = false;
            _deliveryFireCount[i] = 0;
            _deliveryNextFireElapsed[i] = 0f;
        }
    }

    private void InitMovementEventState(SO_Attack_Data data)
    {
        int count = data != null && data.HasMovementEvents ? data.MovementEvents.Length : 0;
        if (_movementStarted == null || _movementStarted.Length < count)
        {
            _movementStarted = new bool[count];
            _movementEnded = new bool[count];
        }

        for (int i = 0; i < count; i++)
        {
            _movementStarted[i] = false;
            _movementEnded[i] = false;
        }
    }

    private void InitFeedbackEventState(SO_Attack_Data data)
    {
        int count = data != null && data.HasFeedbackEvents ? data.FeedbackEvents.Length : 0;
        if (_feedbackFired == null || _feedbackFired.Length < count)
            _feedbackFired = new bool[count];

        for (int i = 0; i < count; i++)
            _feedbackFired[i] = false;
    }

    private void ResetCombo()
    {
        _comboCount = 0;
        _comboTimer = 0f;
        _nextQueued = false;
        _pendingLookDirection = Vector3.zero;
        _skillSequenceAnyHit = false;
        _hitCameraCuePlayed = false;
        _slamLandingFired = false;
        _slamDescending = false;
        _suspendAtApexActive = false;
        if (_deliveryStarted != null)
            for (int i = 0; i < _deliveryStarted.Length; i++)
            {
                _deliveryStarted[i] = false;
                _deliveryEnded[i] = false;
                _deliveryAnyHit[i] = false;
                _deliveryFireCount[i] = 0;
                _deliveryNextFireElapsed[i] = 0f;
            }
        if (_movementStarted != null)
            for (int i = 0; i < _movementStarted.Length; i++)
            {
                _movementStarted[i] = false;
                _movementEnded[i] = false;
            }
        if (_feedbackFired != null)
            for (int i = 0; i < _feedbackFired.Length; i++)
                _feedbackFired[i] = false;
        _attackHitRegistry.Clear();
        _windupActive = false;
        _windupStretch = 1f;
        _playerAnimator?.SetAttackSpeedScale(1f);
        StopFeedbackAfterimages();
        _playerAnimator?.ReleaseLocomotion();
    }

    public void CancelAttack()
    {
        _attackTimer = 0f;
        CancelWindup();
        _moveController?.StopLunge();
        _slamDescending = false;
        _playerAnimator?.ExitAttack();
        StopFeedbackVfx();
        StopFeedbackAfterimages();
        if (_currentData != null)
        {
            // 컷인은 로컬 플레이어만 재생/취소(적 공격 취소가 플레이어의 활성 컷인을 끊지 않게).
            if (PlayerController.IsLocalPlayer(_actionHandler) && AttackTimelineUtility.HasAnyCameraCue(_currentData))
                Game.CancelCameraCutIn();
        }
        _skillSequence = null;
        _skillData = null;
        _skillSequenceIndex = 0;
        ResetCombo();
    }

    private void TickMovementEvents(SO_Attack_Data data, float deltaTime)
    {
        if (data == null || !data.HasMovementEvents)
            return;

        AttackMovementEvent[] movements = data.MovementEvents;
        if (movements == null || movements.Length == 0)
            return;

        float duration = GetAttackDuration(data);
        float elapsed = duration - Mathf.Max(0f, _attackTimer);

        for (int i = 0; i < movements.Length; i++)
        {
            AttackMovementEvent movement = movements[i];
            if (!movement.enabled || _movementEnded[i])
                continue;

            float startTime = Mathf.Max(0f, movement.startTime);
            if (!_movementStarted[i])
            {
                if (elapsed < startTime) continue;
                _movementStarted[i] = true;
                StartMovementEvent(movement);
            }

            if (movement.type == AttackMovementType.Slam)
            {
                TickMovementSlam(data, movement, deltaTime);
                continue;
            }

            if (movement.type == AttackMovementType.Suspend)
            {
                if (movement.duration > 0f && elapsed >= startTime + movement.duration)
                {
                    _suspendAtApexActive = false;
                    _movementEnded[i] = true;
                }
                continue;
            }

            _movementEnded[i] = movement.duration <= 0f || elapsed >= startTime + movement.duration;
        }
    }

    private void TickFeedbackEvents(SO_Attack_Data data)
    {
        if (data == null || !data.HasFeedbackEvents)
            return;

        AttackFeedbackEvent[] events = data.FeedbackEvents;
        float elapsed = GetAttackDuration(data) - Mathf.Max(0f, _attackTimer);
        for (int i = 0; i < events.Length; i++)
        {
            AttackFeedbackEvent feedbackEvent = events[i];
            if (_feedbackFired[i] || !feedbackEvent.enabled || feedbackEvent.trigger != AttackFeedbackTrigger.Timeline)
                continue;
            if (feedbackEvent.deferUntilWindupEnd && _windupActive)
                continue;
            if (elapsed < Mathf.Max(0f, feedbackEvent.startTime))
                continue;

            _feedbackFired[i] = true;
            PlayFeedbackEvent(data, feedbackEvent, default, false, i);
        }

        SweepFeedbackVfxEndTimes(events, elapsed);

        // motionAfterimages 창의 endTime 도달 시 잔상 정지(공격 종료를 기다리지 않음). endTime<=0이면 공격 종료에서 처리.
        if (_afterimageFeedbackActive && _afterimageFeedbackEndElapsed > 0f && elapsed >= _afterimageFeedbackEndElapsed)
            StopFeedbackAfterimages();
    }

    // endTime이 지난 추적 VFX는 공격 종료를 기다리지 않고 개별 디스폰한다(지속 VFX 창).
    private void SweepFeedbackVfxEndTimes(AttackFeedbackEvent[] events, float elapsed)
    {
        for (int i = _feedbackVfxInstances.Count - 1; i >= 0; i--)
        {
            (int eventIndex, AutoDespawn vfx) tracked = _feedbackVfxInstances[i];
            float endTime = tracked.eventIndex >= 0 && tracked.eventIndex < events.Length
                ? events[tracked.eventIndex].endTime
                : 0f;
            if (endTime <= 0f || elapsed < endTime)
                continue;
            DespawnFeedbackVfxInstance(tracked.vfx);
            _feedbackVfxInstances.RemoveAt(i);
        }
    }

    private void PlayDeliveryFeedbackEvents(SO_Attack_Data data, AttackDeliveryEvent delivery, int deliveryIndex)
    {
        if (data == null || !data.HasFeedbackEvents)
            return;

        AttackFeedbackEvent[] events = data.FeedbackEvents;
        for (int i = 0; i < events.Length; i++)
        {
            AttackFeedbackEvent feedbackEvent = events[i];
            if (!feedbackEvent.enabled || feedbackEvent.trigger != AttackFeedbackTrigger.DeliveryFire)
                continue;
            if (feedbackEvent.deliveryIndex >= 0 && feedbackEvent.deliveryIndex != deliveryIndex)
                continue;

            PlayFeedbackEvent(data, feedbackEvent, delivery, true, i);
        }
    }

    private void PlayFeedbackEvent(SO_Attack_Data data, AttackFeedbackEvent feedbackEvent, AttackDeliveryEvent delivery, bool hasDelivery, int feedbackIndex)
    {
        if (feedbackEvent.localPlayerOnly && !PlayerController.IsLocalPlayer(_actionHandler))
            return;

        if (!string.IsNullOrEmpty(feedbackEvent.vfxAddress))
        {
            if (feedbackEvent.vfxOrigin == AttackFeedbackVfxOrigin.DeliveryCenter && hasDelivery)
            {
                Vector3 center = ResolveDeliveryFeedbackCenter(delivery);
                center += transform.rotation * feedbackEvent.vfxOffset;
                CombatFeedback.SpawnVfxAtPosition(feedbackEvent.vfxAddress, center, destroyCancellationToken);
            }
            else
            {
                CastVfxSpace space = feedbackEvent.vfxOrigin == AttackFeedbackVfxOrigin.Actor
                    ? CastVfxSpace.Actor
                    : CastVfxSpace.World;
                SpawnFeedbackVfxAsync(feedbackEvent, space, feedbackIndex).Forget();
            }
        }

        App.PlaySfx(feedbackEvent.sfx, transform.position);

        if (feedbackEvent.cameraShake.enabled)
            App.ShakeCamera(feedbackEvent.cameraShake.amplitude, feedbackEvent.cameraShake.duration, feedbackEvent.cameraShake.frequency);
        if (feedbackEvent.slowMo.duration > 0f)
            TriggerSlowMo(feedbackEvent.slowMo).Forget();
        PlayCameraCue(feedbackEvent.cameraCue, GetAttackDuration(data));

        if (feedbackEvent.motionAfterimages)
        {
            _vfx?.StartMotionAfterimages();
            _afterimageFeedbackActive = true;
            _afterimageFeedbackEndElapsed = feedbackEvent.endTime;
        }
    }

    private Vector3 ResolveDeliveryFeedbackCenter(AttackDeliveryEvent delivery)
    {
        if (delivery.type == AttackDeliveryType.Melee)
            return AttackShapeUtility.GetQueryCenter(transform.position, transform.forward, delivery.melee.hitbox, delivery.melee.shape);
        return transform.position;
    }

    private void PlayCameraCue(AttackCameraCueData cue, float fallbackDuration)
    {
        if (!cue.enabled || !PlayerController.IsLocalPlayer(_actionHandler))
            return;

        Game.PlayCameraCutIn(new SkillCutInData
        {
            enabled = true,
            duration = cue.duration > 0f ? cue.duration : Mathf.Max(0.01f, fallbackDuration),
            fovOverride = cue.fovOverride,
            distanceOverride = cue.distanceOverride,
            heightDelta = cue.heightDelta,
            yawVelocity = cue.yawVelocity
        });
    }

    private void StartMovementEvent(AttackMovementEvent movement)
    {
        switch (movement.type)
        {
            case AttackMovementType.SelfJump:
                _moveController?.Jump(movement.height);
                break;
            case AttackMovementType.Suspend:
                _suspendAtApexActive = true;
                break;
            case AttackMovementType.Slam:
                _slamDescending = true;
                _slamLandingFired = false;
                break;
            case AttackMovementType.Lunge:
                StartMovementLunge(movement);
                break;
        }
    }

    private void StartMovementLunge(AttackMovementEvent movement)
    {
        AttackLungeData lunge = new()
        {
            distance = movement.distance,
            duration = movement.duration,
            speedCurve = movement.curve
        };

        // 이동은 순수 모션만. 잔상 등 연출은 feedbackEvent(motionAfterimages)가 독립적으로 구동한다.
        _moveController?.StartLunge(transform.forward, lunge);
    }

    private void TickMovementSlam(SO_Attack_Data data, AttackMovementEvent movement, float deltaTime)
    {
        if (!_slamDescending)
            return;

        if (_moveController != null && !_moveController.IsGrounded)
        {
            _vertical?.SlamMove(Mathf.Max(0f, movement.speed), deltaTime);
            return;
        }

        if (_slamLandingFired)
            return;

        _slamLandingFired = true;
        _slamDescending = false;
        float duration = GetAttackDuration(data);
        float landingDeliveryTime = AttackTimelineUtility.GetFirstDeliveryStartTime(data);
        _attackTimer = Mathf.Min(_attackTimer, Mathf.Max(0f, duration - landingDeliveryTime));
    }

    private void TickDeliveryEvents(SO_Attack_Data data)
    {
        AttackDeliveryEvent[] deliveries = data.DeliveryEvents;
        if (deliveries == null || deliveries.Length == 0) return;

        float duration = GetAttackDuration(data);
        float elapsed = duration - Mathf.Max(0f, _attackTimer);

        for (int i = 0; i < deliveries.Length; i++)
        {
            AttackDeliveryEvent delivery = deliveries[i];
            if (!delivery.enabled || _deliveryEnded[i])
                continue;

            float startTime = Mathf.Max(0f, delivery.startTime);
            if (!_deliveryStarted[i])
            {
                if (elapsed < startTime) continue;

                _deliveryStarted[i] = true;
                bool didHit = FireDeliveryEvent(data, delivery, i);
                _deliveryAnyHit[i] |= didHit;
                _deliveryFireCount[i] = 1;
                _deliveryNextFireElapsed[i] = startTime + Mathf.Max(0.01f, delivery.melee.repeat.interval);
            }

            TickDeliveryRepeat(data, delivery, i, elapsed, startTime);

            // delivery 활성 창 = melee면 repeat 창(repeat.duration), 그 외(field/projectile)는 스폰 즉시 종료.
            float activeWindow = delivery.type == AttackDeliveryType.Melee ? Mathf.Max(0f, delivery.melee.repeat.duration) : 0f;
            float activeEnd = startTime + activeWindow;
            bool hasActiveWindow = activeWindow > 0f;
            if ((!hasActiveWindow && _deliveryStarted[i]) || (hasActiveWindow && elapsed >= activeEnd))
            {
                _deliveryEnded[i] = true;
                if (delivery.melee.flow.endAttackIfNoHitByEventEnd && !_deliveryAnyHit[i])
                {
                    _attackTimer = 0f;
                    return;
                }
            }
        }
    }

    private void TickDeliveryRepeat(SO_Attack_Data data, AttackDeliveryEvent delivery, int deliveryIndex, float elapsed, float startTime)
    {
        if (delivery.type != AttackDeliveryType.Melee)
            return;

        AttackHitRepeat repeat = delivery.melee.repeat;
        if (!repeat.enabled || repeat.duration <= 0f)
            return;

        float activeEnd = startTime + repeat.duration;
        float interval = Mathf.Max(0.01f, repeat.interval);
        bool limited = repeat.maxCount > 0;
        while (_deliveryNextFireElapsed[deliveryIndex] <= elapsed
               && _deliveryNextFireElapsed[deliveryIndex] <= activeEnd)
        {
            if (limited && _deliveryFireCount[deliveryIndex] >= repeat.maxCount)
                break;

            bool didHit = FireDeliveryEvent(data, delivery, deliveryIndex);
            _deliveryAnyHit[deliveryIndex] |= didHit;
            _deliveryFireCount[deliveryIndex]++;
            _deliveryNextFireElapsed[deliveryIndex] += interval;
        }
    }

    private bool FireDeliveryEvent(SO_Attack_Data data, AttackDeliveryEvent delivery, int deliveryIndex)
    {
        PlayDeliveryFeedbackEvents(data, delivery, deliveryIndex);
        return delivery.type switch
        {
            AttackDeliveryType.Projectile => SpawnDeliveryProjectiles(data, delivery),
            AttackDeliveryType.Field => SpawnDeliveryField(data, delivery),
            _ => FireMeleeDelivery(data, delivery, deliveryIndex)
        };
    }

    private bool FireMeleeDelivery(SO_Attack_Data data, AttackDeliveryEvent delivery, int deliveryIndex)
    {
        float finalDamage = CombatFormula.ScaleAttackDamage(_attackPower, delivery.hitResult.damage);

        bool didHit = _emitter.Emit(
            transform.position, transform.forward, delivery.melee.hitbox, delivery.melee.shape,
            AttackHitInfo.FromHitResult(delivery.hitResult), delivery.hitResult.hitType, finalDamage,
            AttackerFaction, ResolveAttackerEntity(),
            _attackHitRegistry, ResolveDeliveryHitScope(delivery.dedupe, deliveryIndex),
            ShouldDeliveryHitSameTargetOnce(delivery.dedupe), data,
            useFeedbackOverride: true,
            hitSfxOverride: delivery.hitResult.hitSfx,
            hitVfxOverride: delivery.hitResult.hitVfxAddress);

        _skillSequenceAnyHit |= didHit;
        if (!didHit) return false;

        CombatOnHit.ApplyAttackerGains(delivery.hitResult.lifeSteal, finalDamage, _actionHandler, _playerStats != null ? _playerStats.GaugeGainPerDamage : 0f);

        if (delivery.melee.flow.stopMovementOnHit)
            _moveController?.StopLunge();

        PlayLocalDeliveryHitEffects(data, delivery.hitResult);
        return true;
    }

    private bool SpawnDeliveryProjectiles(SO_Attack_Data data, AttackDeliveryEvent delivery)
    {
        AttackProjectileDelivery projectile = delivery.projectile;
        if (string.IsNullOrEmpty(projectile.prefabAddress))
            return false;

        float finalDamage = CombatFormula.ScaleAttackDamage(_attackPower, delivery.hitResult.damage);
        RangedOwner owner = CreateRangedOwner();
        bool spawnFieldOnImpact = TryGetProjectileImpactField(data, out AttackFieldDelivery impactField, out AttackHitResultData impactHitResult, out float impactDuration);
        float impactFinalDamage = spawnFieldOnImpact
            ? CombatFormula.ScaleAttackDamage(_attackPower, impactHitResult.damage)
            : 0f;

        int count = Mathf.Max(1, projectile.count);
        Vector3 forward = ResolveProjectileDirection(projectile);
        Vector3 spawnPos = transform.position + transform.rotation * projectile.spawnOffset;

        if (count == 1)
        {
            SpawnOneDeliveryProjectileAsync(data, projectile, delivery.hitResult, finalDamage, owner, spawnFieldOnImpact, impactField, impactHitResult, impactDuration, impactFinalDamage, spawnPos, forward).Forget();
            return false;
        }

        float spread = projectile.spreadAngle;
        float step, start;
        if (spread >= 360f) { step = 360f / count; start = 0f; }
        else { step = spread / (count - 1); start = -spread * 0.5f; }

        for (int i = 0; i < count; i++)
        {
            Vector3 dir = Quaternion.AngleAxis(start + step * i, Vector3.up) * forward;
            SpawnOneDeliveryProjectileAsync(data, projectile, delivery.hitResult, finalDamage, owner, spawnFieldOnImpact, impactField, impactHitResult, impactDuration, impactFinalDamage, spawnPos, dir).Forget();
        }
        return false;
    }

    private async UniTaskVoid SpawnOneDeliveryProjectileAsync(
        SO_Attack_Data data,
        AttackProjectileDelivery projectileDelivery,
        AttackHitResultData hitResult,
        float finalDamage,
        RangedOwner owner,
        bool spawnFieldOnImpact,
        AttackFieldDelivery impactField,
        AttackHitResultData impactHitResult,
        float impactDuration,
        float impactFinalDamage,
        Vector3 spawnPos,
        Vector3 direction)
    {
        Quaternion rot = Quaternion.LookRotation(direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward);

        Projectile_Hitbox projectile = await App.SpawnAsync<Projectile_Hitbox>(projectileDelivery.prefabAddress, token: destroyCancellationToken);
        if (projectile == null) return;
        projectile.transform.SetPositionAndRotation(spawnPos, rot);
        projectile.Launch(data, projectileDelivery, hitResult, finalDamage, owner, direction, spawnFieldOnImpact, impactField, impactHitResult, impactDuration, impactFinalDamage);
    }

    private bool SpawnDeliveryField(SO_Attack_Data data, AttackDeliveryEvent delivery)
    {
        AttackFieldDelivery field = delivery.field;
        if (field.origin == FieldOrigin.ProjectileImpact || string.IsNullOrEmpty(field.prefabAddress))
            return false;

        float finalDamage = CombatFormula.ScaleAttackDamage(_attackPower, delivery.hitResult.damage);
        SpawnDeliveryFieldAsync(data, field, delivery.hitResult, Mathf.Max(0f, field.lifetime), finalDamage, CreateRangedOwner()).Forget();
        return false;
    }

    private async UniTaskVoid SpawnDeliveryFieldAsync(
        SO_Attack_Data data,
        AttackFieldDelivery field,
        AttackHitResultData hitResult,
        float duration,
        float finalDamage,
        RangedOwner owner)
    {
        Vector3 forward = transform.forward;
        Vector3 spawnPos;
        Transform follow = null;
        if (field.origin == FieldOrigin.AimTarget)
        {
            spawnPos = ResolveAimTargetPosition(owner.Faction, Mathf.Max(0f, field.forwardOffset));
        }
        else
        {
            spawnPos = transform.position + forward * field.forwardOffset;
            follow = field.followAttacker ? transform : null;
        }

        Field_Hitbox instance = await App.SpawnAsync<Field_Hitbox>(field.prefabAddress, token: destroyCancellationToken);
        if (instance == null) return;
        instance.transform.position = spawnPos;
        instance.Activate(data, field, hitResult, duration, finalDamage, owner, forward, follow);
    }

    private bool TryGetProjectileImpactField(
        SO_Attack_Data data,
        out AttackFieldDelivery field,
        out AttackHitResultData hitResult,
        out float duration)
    {
        AttackDeliveryEvent[] deliveries = data.DeliveryEvents;
        if (deliveries != null)
        {
            for (int i = 0; i < deliveries.Length; i++)
            {
                AttackDeliveryEvent delivery = deliveries[i];
                if (!delivery.enabled || delivery.type != AttackDeliveryType.Field)
                    continue;
                if (delivery.field.origin != FieldOrigin.ProjectileImpact || string.IsNullOrEmpty(delivery.field.prefabAddress))
                    continue;

                field = delivery.field;
                hitResult = delivery.hitResult;
                duration = Mathf.Max(0f, delivery.field.lifetime);
                return true;
            }
        }

        field = default;
        hitResult = default;
        duration = 0f;
        return false;
    }

    private RangedOwner CreateRangedOwner()
        => new(
            AttackerFaction,
            ResolveAttackerEntity(),
            _actionHandler,
            _playerStats != null ? _playerStats.GaugeGainPerDamage : 0f);

    private Vector3 ResolveProjectileDirection(AttackProjectileDelivery projectile)
    {
        if (projectile.aimMode == AttackProjectileAimMode.InputDirection && _pendingLookDirection.sqrMagnitude > ExplicitLookInputSqrThreshold)
            return _pendingLookDirection.normalized;

        if (projectile.aimMode == AttackProjectileAimMode.AutoTarget || projectile.aimMode == AttackProjectileAimMode.NearestTarget)
        {
            Vector3 autoAim = FindAutoAimDirection(_currentData);
            if (autoAim.sqrMagnitude > 0.0001f)
                return autoAim.normalized;
        }

        return transform.forward;
    }

    private static int ResolveDeliveryHitScope(AttackHitDeduplication dedupe, int deliveryIndex)
        => dedupe == AttackHitDeduplication.OncePerAttack ? 1 : deliveryIndex + 100;

    private static bool ShouldDeliveryHitSameTargetOnce(AttackHitDeduplication dedupe)
        => dedupe != AttackHitDeduplication.None;

    private void PlayLocalDeliveryHitEffects(SO_Attack_Data data, AttackHitResultData hitResult)
    {
        if (!PlayerController.IsLocalPlayer(_actionHandler))
            return;

        if (!_hitCameraCuePlayed)
        {
            _hitCameraCuePlayed = true;
            if (hitResult.cameraCue.enabled)
                PlayCameraCue(hitResult.cameraCue, GetAttackDuration(data));
        }

        if (hitResult.cameraShake.enabled)
            App.ShakeCamera(hitResult.cameraShake.amplitude, hitResult.cameraShake.duration, hitResult.cameraShake.frequency);
        CombatOnHit.TriggerHitstop(hitResult.hitstop, destroyCancellationToken).Forget();
    }

    public void UpdateLookDirection(Vector3 worldInput)
    {
        // windup(예고) 중엔 조준 방향을 잠근다. 시작 시 확정한 방향을 입력이 덮어쓰지 못하게 해
        // 예고 데칼이 가리킨 방향과 실제 공격(돌진 포함) 방향을 일치시킨다.
        if (_windupActive) return;
        _pendingLookDirection = worldInput;
    }

    // 공격자(이 캐릭터)의 ECS 엔티티. 잡몹이 강제 어그로 대상으로 매칭하는 CharacterNavTarget 엔티티다.
    private Entity ResolveAttackerEntity()
    {
        if (_ecsBridge == null) _ecsBridge = GetComponent<Character_EcsBridge>();
        return _ecsBridge != null ? _ecsBridge.CharacterEntity : Entity.Null;
    }

    private SO_Attack_Data GetData(int index)
        => _attacks != null && _attacks.Length > 0
            ? _attacks[Mathf.Clamp(index, 0, _attacks.Length - 1)]
            : null;


    private static Vector3 ResolveCastVfxScale(Vector3 scale)
        => scale.sqrMagnitude > 0f ? scale : Vector3.one;

    private async UniTaskVoid SpawnFeedbackVfxAsync(AttackFeedbackEvent feedbackEvent, CastVfxSpace space, int feedbackIndex)
    {
        AutoDespawn vfx = await App.SpawnAsync<AutoDespawn>(feedbackEvent.vfxAddress, token: destroyCancellationToken);
        if (vfx == null) return;

        Vector3 scale = ResolveCastVfxScale(feedbackEvent.vfxScale);
        if (space == CastVfxSpace.Actor)
        {
            vfx.transform.SetParent(transform, false);
            vfx.transform.localPosition = feedbackEvent.vfxOffset;
            vfx.transform.localRotation = Quaternion.Euler(feedbackEvent.vfxEuler);
        }
        else
        {
            vfx.transform.SetParent(null, true);
            vfx.transform.position = transform.position + transform.rotation * feedbackEvent.vfxOffset;
            vfx.transform.rotation = transform.rotation * Quaternion.Euler(feedbackEvent.vfxEuler);
        }
        vfx.transform.localScale = scale;
        vfx.Restart();
        if (space == CastVfxSpace.Actor)
            _feedbackVfxInstances.Add((feedbackIndex, vfx));
    }

    private void StopFeedbackVfx()
    {
        for (int i = 0; i < _feedbackVfxInstances.Count; i++)
            DespawnFeedbackVfxInstance(_feedbackVfxInstances[i].vfx);
        _feedbackVfxInstances.Clear();
    }

    private void DespawnFeedbackVfxInstance(AutoDespawn instance)
    {
        if (instance == null) return;
        if (instance.transform.parent != transform) return;
        instance.transform.SetParent(null, true);
        if (instance.gameObject.activeInHierarchy)
            App.Despawn(instance.gameObject);
    }

    private void StopFeedbackAfterimages()
    {
        if (!_afterimageFeedbackActive)
            return;

        _afterimageFeedbackActive = false;
        _afterimageFeedbackEndElapsed = 0f;
        _vfx?.StopMotionAfterimages();
    }

    private async UniTaskVoid TriggerSlowMo(AttackTimeScaleData slowMo)
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
        StopFeedbackVfx();
        _emitter.Dispose();
        if (_cachedWorld != null && _cachedWorld.IsCreated)
            _autoAimQuery.Dispose();
    }

}
