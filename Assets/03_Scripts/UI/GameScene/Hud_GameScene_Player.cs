using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 플레이어 정보 패널.
/// HP, 버프, 이름, 레벨 등 플레이어 상태 시각 피드백을 담당한다.
/// </summary>
public sealed class Hud_GameScene_Player : MonoBehaviour
{
    [Header("HP")]
    [SerializeField] private Image           hpFill;
    [SerializeField] private Image           hpDelayedFill;
    [SerializeField] private TextMeshProUGUI hpAmount;
    [SerializeField] private float           hpDelayedFillDelay = 0.25f;
    [SerializeField] private float           hpDelayedFillSpeed = 0.8f;

    [Header("Gauge")]
    [SerializeField] private Image           gaugeFill;
    [SerializeField] private Image           gaugeDelayedFill;
    [SerializeField] private float           gaugeDelayedFillSpeed = 1.5f;

    private Character_ActionHandler _actionHandler;
    private float                _hpDelayedFillTarget;
    private float                _hpDelayedFillDelayTimer;
    private bool                 _isHpDelayedFillAnimating;
    private float                _gaugeDelayedFillTarget;
    private bool                 _isGaugeDelayedFillAnimating;

    // ───────────────────────────────────────────────
    #region Bind

    public void Bind(Character_ActionHandler actionHandler)
    {
        Unbind();
        _actionHandler = actionHandler;
        if (_actionHandler == null) return;

        _actionHandler.OnHealthChanged += HandleHealthChanged;
        _actionHandler.OnGaugeChanged  += HandleGaugeChanged;
        HandleHealthChanged(_actionHandler.Health, _actionHandler.MaxHealth);
        if (gaugeDelayedFill != null) gaugeDelayedFill.fillAmount = 0f;
        HandleGaugeChanged(_actionHandler.Gauge, _actionHandler.GaugeMax);
    }

    public void Unbind()
    {
        if (_actionHandler == null) return;
        _actionHandler.OnHealthChanged -= HandleHealthChanged;
        _actionHandler.OnGaugeChanged  -= HandleGaugeChanged;
        _actionHandler = null;
    }

    private void OnDestroy() => Unbind();

    #endregion

    // ───────────────────────────────────────────────
    #region HP

    private void Update()
    {
        TickHpDelayedFill();
        TickGaugeDelayedFill();
    }

    private void TickHpDelayedFill()
    {
        if (!_isHpDelayedFillAnimating || hpDelayedFill == null) return;

        if (_hpDelayedFillDelayTimer > 0f)
        {
            _hpDelayedFillDelayTimer -= Time.deltaTime;
            return;
        }

        hpDelayedFill.fillAmount = Mathf.MoveTowards(
            hpDelayedFill.fillAmount,
            _hpDelayedFillTarget,
            hpDelayedFillSpeed * Time.deltaTime);

        if (!Mathf.Approximately(hpDelayedFill.fillAmount, _hpDelayedFillTarget)) return;

        hpDelayedFill.fillAmount  = _hpDelayedFillTarget;
        _isHpDelayedFillAnimating = false;
    }

    private void TickGaugeDelayedFill()
    {
        if (!_isGaugeDelayedFillAnimating || gaugeDelayedFill == null) return;

        gaugeDelayedFill.fillAmount = Mathf.MoveTowards(
            gaugeDelayedFill.fillAmount,
            _gaugeDelayedFillTarget,
            gaugeDelayedFillSpeed * Time.deltaTime);

        if (!Mathf.Approximately(gaugeDelayedFill.fillAmount, _gaugeDelayedFillTarget)) return;

        gaugeDelayedFill.fillAmount   = _gaugeDelayedFillTarget;
        _isGaugeDelayedFillAnimating  = false;
    }

    private void HandleHealthChanged(float current, float max)
    {
        if (hpFill == null) return;

        float fill = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        hpFill.fillAmount = fill;

        if (hpDelayedFill != null)
        {
            if (fill >= hpDelayedFill.fillAmount)
            {
                hpDelayedFill.fillAmount  = fill;
                _isHpDelayedFillAnimating = false;
            }
            else
            {
                _hpDelayedFillTarget      = fill;
                _hpDelayedFillDelayTimer  = hpDelayedFillDelay;
                _isHpDelayedFillAnimating = true;
            }
        }

        if (hpAmount != null)
            hpAmount.text = max > 0f ? $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}" : string.Empty;
    }

    #endregion

    // ───────────────────────────────────────────────
    #region Gauge

    private void HandleGaugeChanged(float current, float max)
    {
        float fill = max > 0f ? Mathf.Clamp01(current / max) : 0f;

        if (gaugeFill != null)
            gaugeFill.fillAmount = fill;

        if (gaugeDelayedFill != null)
        {
            // 증가할 때는 즉시, 감소(Ultimate 소모)할 때만 천천히 따라옴
            if (fill >= gaugeDelayedFill.fillAmount)
            {
                gaugeDelayedFill.fillAmount  = fill;
                _isGaugeDelayedFillAnimating = false;
            }
            else
            {
                _gaugeDelayedFillTarget      = fill;
                _isGaugeDelayedFillAnimating = true;
            }
        }
    }

    #endregion
}
