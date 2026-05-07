using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

/// <summary>
/// Collects local device input and stores it in InputProvider for network ticks to copy.
/// </summary>
public class InputActions_Move : InputActions
{
    private InputAction _moveAction;

#if UNITY_ANDROID || UNITY_IOS
    private Finger _activeFinger;
    private Vector2 _originPos;

    private const float JoystickRadius = 80f;
    private const float DeadZone = 8f;
    private const float ZoneMaxX = 0.4f;
    private const float ZoneMaxY = 0.4f;
#endif

    public override void Connect()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        _moveAction ??= CreateMoveAction();
        _moveAction.Enable();
#endif

#if UNITY_ANDROID || UNITY_IOS
        Touch.onFingerDown += OnFingerDown;
        Touch.onFingerUp += OnFingerUp;
#endif

        InputProvider.ResetMove();
    }

    public override void Disconnect()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        _moveAction?.Disable();
#endif

#if UNITY_ANDROID || UNITY_IOS
        Touch.onFingerDown -= OnFingerDown;
        Touch.onFingerUp -= OnFingerUp;
        _activeFinger = null;
#endif

        InputProvider.ResetMove();
    }

    public override void OnUpdate(float deltaTime)
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        InputProvider.SetMoveDirection(_moveAction.ReadValue<Vector2>());
#endif

#if UNITY_ANDROID || UNITY_IOS
        if (_activeFinger == null) return;

        Vector2 offset = _activeFinger.screenPosition - _originPos;
        Vector2 direction = offset.magnitude < DeadZone
            ? Vector2.zero
            : Vector2.ClampMagnitude(offset / JoystickRadius, 1f);

        InputProvider.SetMoveDirection(direction);
#endif
    }

#if UNITY_EDITOR || UNITY_STANDALONE
    private static InputAction CreateMoveAction()
    {
        var action = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");

        action.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/s")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/a")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/d")
            .With("Right", "<Keyboard>/rightArrow");

        action.AddBinding("<Gamepad>/leftStick");
        action.AddBinding("<Joystick>/stick");

        return action;
    }
#endif

#if UNITY_ANDROID || UNITY_IOS
    private void OnFingerDown(Finger finger)
    {
        if (_activeFinger != null) return;
        if (!IsInZone(finger.screenPosition)) return;
        if (Manager.IsPointerOverUI(finger.screenPosition)) return;

        _activeFinger = finger;
        _originPos = finger.screenPosition;
    }

    private void OnFingerUp(Finger finger)
    {
        if (finger != _activeFinger) return;

        _activeFinger = null;
        InputProvider.SetMoveDirection(Vector2.zero);
    }

    private static bool IsInZone(Vector2 screenPos)
    {
        float nx = screenPos.x / Screen.width;
        float ny = screenPos.y / Screen.height;

        return nx >= 0f && nx <= ZoneMaxX
            && ny >= 0f && ny <= ZoneMaxY;
    }
#endif
}
