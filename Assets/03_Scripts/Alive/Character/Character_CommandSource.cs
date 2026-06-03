using UnityEngine;

public interface Character_CommandSource
{
    Vector3 MoveWorld { get; }
    Vector3 LookWorld { get; }

    bool ConsumeAttack();
    bool ConsumeJump();
    bool ConsumeDash();
    bool ConsumeSkill(int slot);
}
