using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 모바일 전용 입력 버튼 패널.
/// InputManager.IsActive 로 활성화 여부를 확인한 뒤 InputProvider 에 전달한다.
/// </summary>
public sealed class Hud_GameScene_MobileInput : MonoBehaviour
{
    [Header("Move")]
    [SerializeField] private UI_Joystick moveJoystick;

    [Header("Combat")]
    [SerializeField] private Button attackButton;
    [SerializeField] private Button jumpButton;
    [SerializeField] private Button dashButton;

    [Header("Skills (Loadout 4 Slots)")]
    [SerializeField] private Hud_GameScene_SkillSlot[] skillSlots = new Hud_GameScene_SkillSlot[SkillInput.SlotCount];

    private Player_ActionHandler _actionHandler;
    private readonly UnityAction[] _skillClickHandlers = new UnityAction[SkillInput.SlotCount];

    // ───────────────────────────────────────────────
    #region Lifecycle

    private void Awake()
    {
        if (moveJoystick  != null) moveJoystick.OnDirectionChanged += OnMove;

        if (attackButton  != null) attackButton.onClick.AddListener(OnAttack);
        if (jumpButton    != null) jumpButton.onClick.AddListener(OnJump);
        if (dashButton    != null) dashButton.onClick.AddListener(OnDash);

        RegisterSkillSlotButtons();
    }

    private void OnDestroy()
    {
        Unbind();

        if (moveJoystick  != null) moveJoystick.OnDirectionChanged -= OnMove;

        if (attackButton  != null) attackButton.onClick.RemoveListener(OnAttack);
        if (jumpButton    != null) jumpButton.onClick.RemoveListener(OnJump);
        if (dashButton    != null) dashButton.onClick.RemoveListener(OnDash);

        UnregisterSkillSlotButtons();
    }

    #endregion

    // ───────────────────────────────────────────────
    #region Bind

    public void Bind(Player_ActionHandler actionHandler)
    {
        _actionHandler = actionHandler;
        RefreshSkillSlots();
    }

    public void Unbind()
    {
        _actionHandler = null;
        RefreshSkillSlots();
    }

    private void Update()
    {
        UpdateSkillCooldowns();
    }

    private void RefreshSkillSlots()
    {
        if (skillSlots == null) return;

        for (int i = 0; i < skillSlots.Length; i++)
        {
            if (skillSlots[i] == null) continue;

            SO_SkillData skill = _actionHandler != null ? _actionHandler.GetSkillData(i) : null;
            skillSlots[i].Bind(skill);
        }

        UpdateSkillCooldowns();
    }

    private void UpdateSkillCooldowns()
    {
        if (_actionHandler == null || skillSlots == null) return;

        for (int i = 0; i < skillSlots.Length; i++)
        {
            if (skillSlots[i] == null) continue;
            skillSlots[i].SetCooldown(
                _actionHandler.GetSkillCooldown(i),
                _actionHandler.GetSkillCooldownDuration(i));
        }
    }

    private void RegisterSkillSlotButtons()
    {
        if (skillSlots == null) return;

        int count = Mathf.Min(skillSlots.Length, SkillInput.SlotCount);
        for (int i = 0; i < count; i++)
        {
            if (skillSlots[i] == null) continue;

            int slot = i;
            _skillClickHandlers[i] = () => OnSkill(slot);
            skillSlots[i].AddClickListener(_skillClickHandlers[i]);
        }
    }

    private void UnregisterSkillSlotButtons()
    {
        if (skillSlots == null) return;

        int count = Mathf.Min(skillSlots.Length, SkillInput.SlotCount);
        for (int i = 0; i < count; i++)
        {
            if (skillSlots[i] == null || _skillClickHandlers[i] == null) continue;
            skillSlots[i].RemoveClickListener(_skillClickHandlers[i]);
            _skillClickHandlers[i] = null;
        }
    }

    #endregion

    #region Callbacks

    private static void OnMove(Vector2 direction)
    {
        if (!App.IsInputActive<InputActions_Move>()) return;
        InputProvider.SetMoveDirection(direction);
    }

    private static void OnAttack()
    {
        if (!App.IsInputActive<InputActions_Combat>()) return;
        InputProvider.SetAttackPressed();
    }

    private static void OnJump()
    {
        if (!App.IsInputActive<InputActions_Combat>()) return;
        InputProvider.SetJumpPressed();
    }

    private static void OnDash()
    {
        if (!App.IsInputActive<InputActions_Combat>()) return;
        InputProvider.SetDashPressed();
    }

    private static void OnSkill(int slot)
    {
        if (!App.IsInputActive<InputActions_Combat>()) return;
        InputProvider.SetSkillPressed(slot);
    }

    #endregion
}
