using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class InputActions_Camera : InputActions
{
    private InputAction _toggleCameraAction;

#if UNITY_ANDROID || UNITY_IOS
    private Finger _activeFinger;
    private Vector2 _originPos;

    private const float SwipeThreshold = 100f;
    private const float JoystickZoneMaxX = 0.4f;
    private const float JoystickZoneMaxY = 0.4f;
#endif

    public override void Connect()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        _toggleCameraAction ??= CreateToggleCameraAction();
        _toggleCameraAction.performed += OnToggleCameraPerformed;
        _toggleCameraAction.Enable();
#endif

#if UNITY_ANDROID || UNITY_IOS
        Touch.onFingerDown += OnFingerDown;
        Touch.onFingerUp += OnFingerUp;
#endif
    }

    public override void Disconnect()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (_toggleCameraAction != null)
        {
            _toggleCameraAction.performed -= OnToggleCameraPerformed;
            _toggleCameraAction.Disable();
        }
#endif

#if UNITY_ANDROID || UNITY_IOS
        Touch.onFingerDown -= OnFingerDown;
        Touch.onFingerUp -= OnFingerUp;
        _activeFinger = null;
#endif
    }

    public override void OnUpdate(float deltaTime) { }

#if UNITY_EDITOR || UNITY_STANDALONE
    private static InputAction CreateToggleCameraAction()
    {
        var action = new InputAction("Toggle Camera", InputActionType.Button);
        action.AddBinding("<Keyboard>/c");
        action.AddBinding("<Gamepad>/rightStickPress");
        return action;
    }

    private static void OnToggleCameraPerformed(InputAction.CallbackContext _)
        => App.ToggleCombatCameraMode();
#endif

#if UNITY_ANDROID || UNITY_IOS
    private void OnFingerDown(Finger finger)
    {
        if (_activeFinger != null) return;
        if (Manager.IsPointerOverUI(finger.screenPosition)) return;
        if (IsInJoystickZone(finger.screenPosition)) return;

        _activeFinger = finger;
        _originPos = finger.screenPosition;
    }

    private void OnFingerUp(Finger finger)
    {
        if (finger != _activeFinger) return;
        _activeFinger = null;

        Vector2 delta = finger.screenPosition - _originPos;
        if (Mathf.Abs(delta.y) < SwipeThreshold) return;
        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)) return;

        App.SetCombatCameraMode(delta.y > 0f
            ? CombatCameraMode.ThirdPerson
            : CombatCameraMode.Tactical);
    }

    private static bool IsInJoystickZone(Vector2 screenPos)
    {
        float nx = screenPos.x / Screen.width;
        float ny = screenPos.y / Screen.height;
        return nx <= JoystickZoneMaxX && ny <= JoystickZoneMaxY;
    }
#endif
}
