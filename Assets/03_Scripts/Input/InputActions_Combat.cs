using UnityEngine.InputSystem;

public class InputActions_Combat : InputActions
{
    private InputAction _attackAction;
    private InputAction _jumpAction;
    private InputAction _dashAction;
    private InputAction _skill0Action;
    private InputAction _skill1Action;
    private InputAction _skill2Action;
    private InputAction _skill3Action;

    public override void Connect()
    {
        _attackAction ??= CreateAttackAction();
        _jumpAction   ??= CreateJumpAction();
        _dashAction   ??= CreateDashAction();
        _skill0Action ??= CreateSkillAction(0, "<Keyboard>/u", "<Gamepad>/dpad/up");
        _skill1Action ??= CreateSkillAction(1, "<Keyboard>/i", "<Gamepad>/dpad/right");
        _skill2Action ??= CreateSkillAction(2, "<Keyboard>/o", "<Gamepad>/dpad/down");
        _skill3Action ??= CreateSkillAction(3, "<Keyboard>/p", "<Gamepad>/dpad/left");

        _attackAction.performed += OnAttackPerformed;
        _jumpAction.performed   += OnJumpPerformed;
        _dashAction.performed   += OnDashPerformed;
        _skill0Action.performed += OnSkill0Performed;
        _skill1Action.performed += OnSkill1Performed;
        _skill2Action.performed += OnSkill2Performed;
        _skill3Action.performed += OnSkill3Performed;

        _attackAction.Enable();
        _jumpAction.Enable();
        _dashAction.Enable();
        _skill0Action.Enable();
        _skill1Action.Enable();
        _skill2Action.Enable();
        _skill3Action.Enable();

        InputProvider.ResetCombat();
        InputProvider.ResetSkills();
    }

    public override void Disconnect()
    {
        if (_attackAction != null) { _attackAction.performed -= OnAttackPerformed; _attackAction.Disable(); }
        if (_jumpAction   != null) { _jumpAction.performed   -= OnJumpPerformed;   _jumpAction.Disable();   }
        if (_dashAction   != null) { _dashAction.performed   -= OnDashPerformed;   _dashAction.Disable();   }
        if (_skill0Action != null) { _skill0Action.performed -= OnSkill0Performed; _skill0Action.Disable(); }
        if (_skill1Action != null) { _skill1Action.performed -= OnSkill1Performed; _skill1Action.Disable(); }
        if (_skill2Action != null) { _skill2Action.performed -= OnSkill2Performed; _skill2Action.Disable(); }
        if (_skill3Action != null) { _skill3Action.performed -= OnSkill3Performed; _skill3Action.Disable(); }

        InputProvider.ResetCombat();
        InputProvider.ResetSkills();
    }

    public override void OnUpdate(float deltaTime) { }

    private static InputAction CreateAttackAction()
    {
        var action = new InputAction("Attack", InputActionType.Button);
        action.AddBinding("<Keyboard>/j");
        action.AddBinding("<Gamepad>/buttonSouth");
        return action;
    }

    private static InputAction CreateJumpAction()
    {
        var action = new InputAction("Jump", InputActionType.Button);
        action.AddBinding("<Keyboard>/space");
        action.AddBinding("<Gamepad>/buttonNorth");
        return action;
    }

    private static InputAction CreateDashAction()
    {
        var action = new InputAction("Dash", InputActionType.Button);
        action.AddBinding("<Keyboard>/k");
        action.AddBinding("<Gamepad>/rightShoulder");
        return action;
    }

    private static InputAction CreateSkillAction(int slot, string keyboardBinding, string gamepadBinding)
    {
        var action = new InputAction($"Skill{slot}", InputActionType.Button);
        action.AddBinding(keyboardBinding);
        action.AddBinding(gamepadBinding);
        return action;
    }

    private static void OnAttackPerformed(InputAction.CallbackContext _) => InputProvider.SetAttackPressed();
    private static void OnJumpPerformed  (InputAction.CallbackContext _) => InputProvider.SetJumpPressed();
    private static void OnDashPerformed  (InputAction.CallbackContext _) => InputProvider.SetDashPressed();
    private static void OnSkill0Performed(InputAction.CallbackContext _) => InputProvider.SetSkillPressed(0);
    private static void OnSkill1Performed(InputAction.CallbackContext _) => InputProvider.SetSkillPressed(1);
    private static void OnSkill2Performed(InputAction.CallbackContext _) => InputProvider.SetSkillPressed(2);
    private static void OnSkill3Performed(InputAction.CallbackContext _) => InputProvider.SetSkillPressed(3);
}
