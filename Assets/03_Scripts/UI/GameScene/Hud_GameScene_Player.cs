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

    private Player_ActionHandler _actionHandler;
    private float                _hpDelayedFillTarget;
    private float                _hpDelayedFillDelayTimer;
    private bool                 _isHpDelayedFillAnimating;

    // ───────────────────────────────────────────────
    #region Bind

    public void Bind(Player_ActionHandler actionHandler)
    {
        Unbind();
        _actionHandler = actionHandler;
        if (_actionHandler == null) return;

        _actionHandler.OnHealthChanged += HandleHealthChanged;
        HandleHealthChanged(_actionHandler.Health, _actionHandler.MaxHealth);
    }

    public void Unbind()
    {
        if (_actionHandler == null) return;
        _actionHandler.OnHealthChanged -= HandleHealthChanged;
        _actionHandler = null;
    }

    private void OnDestroy() => Unbind();

    #endregion

    // ───────────────────────────────────────────────
    #region HP

    private void Update()
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
            hpAmount.text = max > 0f ? $"{current} / {max}" : string.Empty;
    }

    #endregion
}
