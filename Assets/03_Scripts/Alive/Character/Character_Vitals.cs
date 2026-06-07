using System;
using MapNav.Ecs;
using UnityEngine;

// 플레이어/장수가 공유하는 전투 상태(체력·게이지·진영·사망)의 단일 진실.
// 숫자와 이벤트만 보유한다. 피격 라우팅(IDamageable/IHitTarget)과 반응(넉백/다운/사망 연출)은
// 같은 GameObject의 Character_ActionHandler가 담당하고, 여기서는 그 결과를 숫자로만 반영한다.
// 장수는 Elite_Embodiment가 이 컴포넌트를 구독해 Elite_State(백그라운드 진실)에 미러한다.
[DisallowMultipleComponent]
public sealed class Character_Vitals : MonoBehaviour
{
    private float _health;
    private float _maxHealth = 100f;
    private float _gauge;
    private float _gaugeMax = 100f;
    private float _defense;
    private float _break;
    private float _breakMax;
    private float _breakRecoveryDelay = 1.5f;
    private float _breakRecoveryPerSecond = 60f;
    private float _brokenDuration = 1.5f;
    private float _breakRecoveryRatioOnBrokenEnd = 1f;
    private float _breakRecoveryTimer;
    private float _bodyRadius = 0.5f;
    private NavFaction _faction = NavFaction.Ally;
    private bool _isDead;
    private bool _isBroken;
    private bool _factionResolved;

    // 진영이 명시적으로 주입(Configure)되기 전에는 false. ECS 다리(Character_EcsBridge)가 이 값으로
    // "진영 미확정" 캐릭터를 잡몹의 타겟·타격 후보에서 제외한다(아군 엘리트가 입장 첫 프레임에 잠정
    // Enemy로 노출돼 아군 잡몹이 몰리는 것을 막는다).
    public bool FactionResolved => _factionResolved;

    public float Health => _health;
    public float MaxHealth => _maxHealth;
    public float Gauge => _gauge;
    public float GaugeMax => _gaugeMax;
    public float Break => _break;
    public float BreakMax => _breakMax;
    public float BrokenDuration => _brokenDuration;
    public bool IsBroken => _isBroken;
    public float Defense => _defense;
    // 잡몹이 못 들어오는 몸체 반경 + 잡몹 공격 적중 판정 반경(공용). Character_EcsBridge가 ECS로 발행한다.
    public float BodyRadius => _bodyRadius;
    public NavFaction Faction => _faction;
    public bool IsDead => _isDead;

    // HUD/디버그가 구독한다. (current, max)를 발사. OnDied는 체력이 0이 되는 순간 1회만.
    public event Action<float, float> OnHealthChanged;
    public event Action<float, float> OnGaugeChanged;
    public event Action<float, float> OnBreakChanged;
    public event Action OnBroken;
    public event Action OnDied;

    // 스탯/진영을 주입하고 체력을 (재)설정한다. startHealth가 null이면 풀피로 시작.
    // 장수 재실체화 시 Elite_State.Health를 startHealth로 넘겨 직전 체력을 복원한다.
    // 이벤트 구독자는 유지된다(Configure는 숫자만 갱신).
    public void Configure(
        float maxHealth,
        float defense,
        float gaugeMax,
        NavFaction faction,
        float? startHealth = null,
        float bodyRadius = 0.5f,
        float breakMax = 0f,
        float breakRecoveryDelay = 1.5f,
        float breakRecoveryPerSecond = 60f,
        float brokenDuration = 1.5f,
        float breakRecoveryRatioOnBrokenEnd = 1f)
    {
        _maxHealth  = Mathf.Max(1f, maxHealth);
        _defense    = defense;
        _gaugeMax   = Mathf.Max(0f, gaugeMax);
        _bodyRadius = Mathf.Max(0f, bodyRadius);
        _breakMax = Mathf.Max(0f, breakMax);
        _break = _breakMax;
        _breakRecoveryDelay = Mathf.Max(0f, breakRecoveryDelay);
        _breakRecoveryPerSecond = Mathf.Max(0f, breakRecoveryPerSecond);
        _brokenDuration = Mathf.Max(0f, brokenDuration);
        _breakRecoveryRatioOnBrokenEnd = Mathf.Clamp01(breakRecoveryRatioOnBrokenEnd);
        _breakRecoveryTimer = 0f;
        _faction    = faction;
        _factionResolved = true;
        _isDead    = false;
        _isBroken  = false;
        _health    = Mathf.Clamp(startHealth ?? _maxHealth, 0f, _maxHealth);

        OnHealthChanged?.Invoke(_health, _maxHealth);
        OnGaugeChanged?.Invoke(_gauge, _gaugeMax);
        OnBreakChanged?.Invoke(_break, _breakMax);
    }

    private void Update()
    {
        if (_isDead || _breakMax <= 0f || Mathf.Approximately(_break, _breakMax))
            return;

        if (_breakRecoveryTimer > 0f)
        {
            _breakRecoveryTimer -= Time.deltaTime;
            return;
        }

        if (_isBroken)
        {
            _isBroken = false;
            float recovered = Mathf.Max(_break, _breakMax * _breakRecoveryRatioOnBrokenEnd);
            if (!Mathf.Approximately(recovered, _break))
            {
                _break = recovered;
                OnBreakChanged?.Invoke(_break, _breakMax);
            }
        }

        float next = Mathf.Min(_breakMax, _break + _breakRecoveryPerSecond * Time.deltaTime);
        if (Mathf.Approximately(next, _break)) return;

        _break = next;
        OnBreakChanged?.Invoke(_break, _breakMax);
    }

    // 방어력 감산을 적용해 체력을 깎는다. 0이 되면 OnDied를 1회 발행.
    public void ApplyDamage(float amount)
    {
        if (_isDead || amount <= 0f) return;
        SetHealthInternal(_health - CombatFormula.ReduceIncomingDamage(_defense, amount));
    }

    public void Heal(float amount)
    {
        if (_isDead || amount <= 0f) return;
        SetHealthInternal(_health + amount);
    }

    public void AddGauge(float amount)
    {
        if (_isDead || amount <= 0f) return;

        float next = Mathf.Min(_gaugeMax, _gauge + amount);
        if (Mathf.Approximately(next, _gauge)) return;

        _gauge = next;
        OnGaugeChanged?.Invoke(_gauge, _gaugeMax);
    }

    public bool ApplyBreakDamage(float amount)
    {
        if (_isDead || _breakMax <= 0f || amount <= 0f || _isBroken)
            return false;

        _break = Mathf.Max(0f, _break - amount);
        _breakRecoveryTimer = _breakRecoveryDelay;
        OnBreakChanged?.Invoke(_break, _breakMax);

        if (_break > 0f) return false;

        _isBroken = true;
        _breakRecoveryTimer = _brokenDuration;
        OnBroken?.Invoke();
        return true;
    }

    public bool TryConsumeGauge(float cost)
    {
        if (cost <= 0f) return true;
        if (_gauge < cost) return false;

        _gauge -= cost;
        OnGaugeChanged?.Invoke(_gauge, _gaugeMax);
        return true;
    }

    private void SetHealthInternal(float value)
    {
        float next = Mathf.Clamp(value, 0f, _maxHealth);
        if (Mathf.Approximately(next, _health)) return;

        _health = next;
        OnHealthChanged?.Invoke(_health, _maxHealth);

        if (_health <= 0f && !_isDead)
        {
            _isDead = true;
            _isBroken = false;
            OnDied?.Invoke();
        }
    }
}
