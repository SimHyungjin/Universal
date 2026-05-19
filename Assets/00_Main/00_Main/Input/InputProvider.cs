using UnityEngine;

public struct MoveInput
{
    public Vector2 Direction;
}

public struct CombatInput
{
    public bool AttackPressed;
    public bool JumpPressed;
    public bool DashPressed;
}

public static class InputProvider
{
    public static MoveInput Move;
    public static CombatInput Combat;

    public static void SetMoveDirection(Vector2 direction)
    {
        Move.Direction = Vector2.ClampMagnitude(direction, 1f);
    }

    public static void ResetMove()
    {
        Move.Direction = Vector2.zero;
    }

    public static void SetAttackPressed() => Combat.AttackPressed = true;
    public static void SetJumpPressed() => Combat.JumpPressed = true;
    public static void SetDashPressed() => Combat.DashPressed = true;

    public static bool ConsumeAttack()
    {
        bool v = Combat.AttackPressed;
        Combat.AttackPressed = false;
        return v;
    }

    public static bool ConsumeJump()
    {
        bool v = Combat.JumpPressed;
        Combat.JumpPressed = false;
        return v;
    }

    public static bool ConsumeDash()
    {
        bool v = Combat.DashPressed;
        Combat.DashPressed = false;
        return v;
    }

    public static void ResetCombat() => Combat = default;
}
