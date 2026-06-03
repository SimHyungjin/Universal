using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed class Hud_GameScene_SkillSlot : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private Button button;

    [Header("Visuals")]
    [SerializeField] private Image icon;
    [SerializeField] private Image cooldownDim;
    [SerializeField] private Image cooldownFill;
    [SerializeField] private Image frame;
    [SerializeField] private Image frameFill;
    [SerializeField] private TextMeshProUGUI cooldownText;

    [Header("Options")]
    [SerializeField] private bool hideWhenEmpty;
    [SerializeField] private bool frameFillsAsCooldownRecovers = true;
    [SerializeField, Range(0f, 1f)] private float emptyAlpha = 0.35f;
    [SerializeField, Min(0f)] private float cooldownVisibleThreshold = 0.05f;

    private SO_Skill_Data _skill;
    private float _cooldownDuration;

    public Button Button
    {
        get
        {
            if (button == null)
                button = GetComponent<Button>();
            return button;
        }
    }

    private void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();
    }

    public void AddClickListener(UnityAction callback)
    {
        if (callback == null) return;

        Button target = Button;
        if (target != null)
            target.onClick.AddListener(callback);
    }

    public void RemoveClickListener(UnityAction callback)
    {
        if (callback == null) return;

        Button target = Button;
        if (target != null)
            target.onClick.RemoveListener(callback);
    }

    public void Bind(SO_Skill_Data skill)
    {
        _skill = skill;
        _cooldownDuration = skill != null ? Mathf.Max(0f, skill.Cooldown) : 0f;

        ApplySkillVisual();
        SetCooldown(0f, _cooldownDuration);
    }

    public void SetCooldown(float remaining, float duration)
    {
        _cooldownDuration = Mathf.Max(0f, duration);

        remaining = Mathf.Max(0f, remaining);
        float ratio = _cooldownDuration > 0f ? Mathf.Clamp01(remaining / _cooldownDuration) : 0f;
        bool isCoolingDown = remaining > cooldownVisibleThreshold && ratio > 0f;

        SetGraphicEnabled(cooldownDim, isCoolingDown);
        SetGraphicEnabled(cooldownFill, isCoolingDown);
        SetGraphicEnabled(cooldownText, isCoolingDown);

        if (cooldownFill != null)
            cooldownFill.fillAmount = ratio;

        if (frameFill != null)
            frameFill.fillAmount = isCoolingDown
                ? frameFillsAsCooldownRecovers ? 1f - ratio : ratio
                : 1f;

        if (cooldownText != null)
            cooldownText.text = isCoolingDown ? Mathf.CeilToInt(remaining).ToString() : string.Empty;
    }

    public void Clear()
    {
        Bind(null);
    }

    private void ApplySkillVisual()
    {
        bool hasSkill = _skill != null;

        if (hideWhenEmpty && gameObject.activeSelf != hasSkill)
            gameObject.SetActive(hasSkill);

        if (icon != null)
        {
            icon.sprite = hasSkill ? _skill.Icon : null;
            icon.enabled = hasSkill && _skill.Icon != null;
            SetGraphicAlpha(icon, hasSkill ? 1f : emptyAlpha);
        }

        SetGraphicAlpha(frame, hasSkill ? 1f : emptyAlpha);
        SetGraphicAlpha(frameFill, hasSkill ? 1f : 0f);
        SetCooldown(0f, _cooldownDuration);
    }

    private static void SetGraphicEnabled(Graphic graphic, bool enabled)
    {
        if (graphic != null)
            graphic.enabled = enabled;
    }

    private static void SetGraphicAlpha(Graphic graphic, float alpha)
    {
        if (graphic == null) return;

        Color color = graphic.color;
        color.a = alpha;
        graphic.color = color;
    }
}
