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

// 스킬 슬롯 로드아웃. 4개 고정 — [[project_skill_loadout]] 참조.
public struct SkillInput
{
    public const int SlotCount = 4;
    public bool Slot0Pressed;
    public bool Slot1Pressed;
    public bool Slot2Pressed;
    public bool Slot3Pressed;
}

public static class InputProvider
{
    public static MoveInput Move;
    public static CombatInput Combat;
    public static SkillInput Skill;

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

    public static void SetSkillPressed(int slot)
    {
        switch (slot)
        {
            case 0: Skill.Slot0Pressed = true; break;
            case 1: Skill.Slot1Pressed = true; break;
            case 2: Skill.Slot2Pressed = true; break;
            case 3: Skill.Slot3Pressed = true; break;
        }
    }

    public static bool ConsumeSkill(int slot)
    {
        switch (slot)
        {
            case 0:
                bool v0 = Skill.Slot0Pressed;
                Skill.Slot0Pressed = false;
                return v0;
            case 1:
                bool v1 = Skill.Slot1Pressed;
                Skill.Slot1Pressed = false;
                return v1;
            case 2:
                bool v2 = Skill.Slot2Pressed;
                Skill.Slot2Pressed = false;
                return v2;
            case 3:
                bool v3 = Skill.Slot3Pressed;
                Skill.Slot3Pressed = false;
                return v3;
            default: return false;
        }
    }

    public static void ResetSkills() => Skill = default;
}
