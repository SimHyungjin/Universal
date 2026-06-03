using UnityEngine;

[DisallowMultipleComponent]
public sealed class Elite_AICommandSource : MonoBehaviour, Character_CommandSource
{
    private readonly bool[] _skillPressed = new bool[SkillInput.SlotCount];
    private Vector3 _moveWorld;
    private Vector3 _lookWorld;
    private bool _attackPressed;
    private bool _jumpPressed;
    private bool _dashPressed;

    public Vector3 MoveWorld => _moveWorld;
    public Vector3 LookWorld => _lookWorld.sqrMagnitude > 0.0001f ? _lookWorld : _moveWorld;

    public void SetMoveWorld(Vector3 value)
    {
        value.y = 0f;
        _moveWorld = Vector3.ClampMagnitude(value, 1f);
    }

    public void SetLookWorld(Vector3 value)
    {
        value.y = 0f;
        _lookWorld = value.sqrMagnitude > 0.0001f ? value.normalized : Vector3.zero;
    }

    public void PressAttack() => _attackPressed = true;
    public void PressJump() => _jumpPressed = true;
    public void PressDash() => _dashPressed = true;

    public void PressSkill(int slot)
    {
        if (slot >= 0 && slot < _skillPressed.Length)
            _skillPressed[slot] = true;
    }

    public bool ConsumeAttack()
    {
        bool value = _attackPressed;
        _attackPressed = false;
        return value;
    }

    public bool ConsumeJump()
    {
        bool value = _jumpPressed;
        _jumpPressed = false;
        return value;
    }

    public bool ConsumeDash()
    {
        bool value = _dashPressed;
        _dashPressed = false;
        return value;
    }

    public bool ConsumeSkill(int slot)
    {
        if (slot < 0 || slot >= _skillPressed.Length)
            return false;

        bool value = _skillPressed[slot];
        _skillPressed[slot] = false;
        return value;
    }
}
