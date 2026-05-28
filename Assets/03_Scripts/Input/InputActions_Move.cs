using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Collects local device input and stores it in InputProvider for network ticks to copy.
/// </summary>
public class InputActions_Move : InputActions
{
    private InputAction _moveAction;

    public override void Connect()
    {
        _moveAction ??= CreateMoveAction();
        _moveAction.Enable();
        InputProvider.ResetMove();
    }

    public override void Disconnect()
    {
        _moveAction?.Disable();
        InputProvider.ResetMove();
    }

    private bool _wasPressing;

    public override void OnUpdate(float deltaTime)
    {
        var  dir      = _moveAction.ReadValue<Vector2>();
        bool pressing = dir.sqrMagnitude > 0.01f;

        if (pressing)
        {
            InputProvider.SetMoveDirection(dir);
            _wasPressing = true;
        }
        else if (_wasPressing)
        {
            // 키보드/패드를 막 뗀 프레임에만 리셋 → 조이스틱 값을 매 프레임 덮지 않음
            InputProvider.SetMoveDirection(Vector2.zero);
            _wasPressing = false;
        }
    }

    private static InputAction CreateMoveAction()
    {
        var action = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");

        action.AddCompositeBinding("2DVector")
            .With("Up",    "<Keyboard>/w")
            .With("Up",    "<Keyboard>/upArrow")
            .With("Down",  "<Keyboard>/s")
            .With("Down",  "<Keyboard>/downArrow")
            .With("Left",  "<Keyboard>/a")
            .With("Left",  "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/d")
            .With("Right", "<Keyboard>/rightArrow");

        action.AddBinding("<Gamepad>/leftStick");
        action.AddBinding("<Joystick>/stick");

        return action;
    }
}
