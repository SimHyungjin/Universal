using UnityEngine;

// 플레이어 입력을 Character_CommandSource로 노출하는 어댑터. 전역 입력(Main.Input)만 읽어
// Unity 컴포넌트일 필요가 없다 — PlayerController가 1개 인스턴스를 들고 빙의한 캐릭터에 주입한다.
// AI는 Elite_AICommandSource가 같은 인터페이스를 채우므로, 캐릭터 입장에선 소스 교체만으로 조종 주체가 바뀐다.
public sealed class Player_InputCommandSource : Character_CommandSource
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
