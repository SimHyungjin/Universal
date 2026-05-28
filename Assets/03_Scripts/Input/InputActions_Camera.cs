using UnityEngine.InputSystem;

public class InputActions_Camera : InputActions
{
    private InputAction _toggleCameraAction;

    public override void Connect()
    {
        _toggleCameraAction ??= CreateToggleCameraAction();
        _toggleCameraAction.performed += OnToggleCameraPerformed;
        _toggleCameraAction.Enable();
    }

    public override void Disconnect()
    {
        if (_toggleCameraAction == null) return;
        _toggleCameraAction.performed -= OnToggleCameraPerformed;
        _toggleCameraAction.Disable();
    }

    public override void OnUpdate(float deltaTime) { }

    private static InputAction CreateToggleCameraAction()
    {
        var action = new InputAction("Toggle Camera", InputActionType.Button);
        action.AddBinding("<Keyboard>/c");
        action.AddBinding("<Gamepad>/rightStickPress");
        return action;
    }

    private static void OnToggleCameraPerformed(InputAction.CallbackContext _)
        => App.ToggleCombatCameraMode();
}
