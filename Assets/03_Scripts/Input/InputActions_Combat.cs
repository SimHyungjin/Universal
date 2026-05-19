using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class InputActions_Combat : InputActions
{
    private InputAction _attackAction;
    private InputAction _jumpAction;
    private InputAction _dashAction;

#if UNITY_ANDROID || UNITY_IOS
    private const float ZoneMinX = 0.5f;
    private const float ZoneMaxY = 0.5f;
#endif

    public override void Connect()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        _attackAction ??= CreateAttackAction();
        _jumpAction ??= CreateJumpAction();
        _dashAction ??= CreateDashAction();
        _attackAction.performed += OnAttackPerformed;
        _jumpAction.performed += OnJumpPerformed;
        _dashAction.performed += OnDashPerformed;
        _attackAction.Enable();
        _jumpAction.Enable();
        _dashAction.Enable();
#endif

#if UNITY_ANDROID || UNITY_IOS
        Touch.onFingerDown += OnFingerDown;
#endif

        InputProvider.ResetCombat();
    }

    public override void Disconnect()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (_attackAction != null)
        {
            _attackAction.performed -= OnAttackPerformed;
            _attackAction.Disable();
        }
        if (_jumpAction != null)
        {
            _jumpAction.performed -= OnJumpPerformed;
            _jumpAction.Disable();
        }
        if (_dashAction != null)
        {
            _dashAction.performed -= OnDashPerformed;
            _dashAction.Disable();
        }
#endif

#if UNITY_ANDROID || UNITY_IOS
        Touch.onFingerDown -= OnFingerDown;
#endif

        InputProvider.ResetCombat();
    }

    public override void OnUpdate(float deltaTime) { }

#if UNITY_EDITOR || UNITY_STANDALONE
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

    private static void OnAttackPerformed(InputAction.CallbackContext _)
        => InputProvider.SetAttackPressed();

    private static void OnJumpPerformed(InputAction.CallbackContext _)
        => InputProvider.SetJumpPressed();

    private static void OnDashPerformed(InputAction.CallbackContext _)
        => InputProvider.SetDashPressed();
#endif

#if UNITY_ANDROID || UNITY_IOS
    private void OnFingerDown(Finger finger)
    {
        if (Manager.IsPointerOverUI(finger.screenPosition)) return;

        float nx = finger.screenPosition.x / Screen.width;
        float ny = finger.screenPosition.y / Screen.height;
        if (nx >= ZoneMinX && ny <= ZoneMaxY)
            InputProvider.SetAttackPressed();
    }
#endif
}
