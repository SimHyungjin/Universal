/// <summary>
/// 인게임 씬 메인 HUD 코디네이터.
/// 하위 컴포넌트를 조립하고 외부 Bind 호출을 위임한다.
/// </summary>
public sealed class Hud_GameScene : UI_Hud
{
    [UnityEngine.SerializeField] private Hud_GameScene_Player player;
    [UnityEngine.SerializeField] private Hud_GameScene_MobileInput mobileInput;

    public void Bind(Player_ActionHandler actionHandler)
    {
        Initialize();
        if (player != null) player.Bind(actionHandler);
        if (mobileInput != null) mobileInput.Bind(actionHandler);
    }

    public void Unbind()
    {
        Initialize();
        if (player != null) player.Unbind();
        if (mobileInput != null) mobileInput.Unbind();
    }

    public override bool Initialize()
    {
        if (player == null) player = GetComponentInChildren<Hud_GameScene_Player>(true);
        if (mobileInput == null) mobileInput = GetComponentInChildren<Hud_GameScene_MobileInput>(true);
        return base.Initialize();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        Unbind();
    }
}
