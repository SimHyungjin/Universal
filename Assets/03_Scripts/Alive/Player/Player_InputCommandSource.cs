using UnityEngine;

[DisallowMultipleComponent]
public sealed class Player_InputCommandSource : MonoBehaviour, Character_CommandSource
{
    public Vector3 MoveWorld => IsMoveActive()
        ? Character_MoveController.GetCameraRelativeInput(InputProvider.Move.Direction)
        : Vector3.zero;

    public Vector3 LookWorld => MoveWorld;

    public bool ConsumeAttack()
        => IsCombatActive() && InputProvider.ConsumeAttack();

    public bool ConsumeJump()
        => IsCombatActive() && InputProvider.ConsumeJump();

    public bool ConsumeDash()
        => IsCombatActive() && InputProvider.ConsumeDash();

    public bool ConsumeSkill(int slot)
        => IsCombatActive() && InputProvider.ConsumeSkill(slot);

    private static bool IsMoveActive()
        => Main.Input != null && Main.Input.IsActive<InputActions_Move>();

    private static bool IsCombatActive()
        => Main.Input != null && Main.Input.IsActive<InputActions_Combat>();
}
