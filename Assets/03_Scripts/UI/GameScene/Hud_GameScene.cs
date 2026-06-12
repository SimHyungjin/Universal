using UnityEngine;

/// <summary>
/// In-game HUD coordinator.
/// </summary>
public sealed class Hud_GameScene : UI_Hud
{
    [SerializeField] private Hud_GameScene_Player player;
    [SerializeField] private Hud_GameScene_MobileInput mobileInput;
    [SerializeField] private Hud_GameScene_Minimap minimap;

    private bool _createdMinimap;

    public void Bind(Character_ActionHandler actionHandler)
    {
        Initialize();
        if (player != null) player.Bind(actionHandler);
        if (mobileInput != null) mobileInput.Bind(actionHandler);
    }

    public void BindMinimap(MinimapModel model, Transform playerTransform, SO_Character_Data playerData)
    {
        Initialize();
        if (model == null) return;

        if (minimap == null)
        {
            minimap = gameObject.AddComponent<Hud_GameScene_Minimap>();
            _createdMinimap = true;
        }

        Canvas.ForceUpdateCanvases();
        minimap.Init(model);
        minimap.AddPlayerMarker(playerTransform, playerData);
    }

    public void BindEliteManager(Elite_Manager eliteManager)
    {
        Initialize();
        eliteManager?.BindMinimap(minimap);
    }

    // 본진(적 코어)을 미니맵에 마커로 표시 — 결전 목표 위치를 항상 보이게.
    public void BindCapital(Sector capital)
    {
        Initialize();
        if (minimap != null) minimap.SetCapital(capital);
    }

    public void Unbind()
    {
        Initialize();
        if (player != null) player.Unbind();
        if (mobileInput != null) mobileInput.Unbind();
        ClearMinimap();
    }

    public override bool Initialize()
    {
        if (player == null) player = GetComponentInChildren<Hud_GameScene_Player>(true);
        if (mobileInput == null) mobileInput = GetComponentInChildren<Hud_GameScene_MobileInput>(true);
        if (minimap == null) minimap = GetComponentInChildren<Hud_GameScene_Minimap>(true);
        return base.Initialize();
    }

    private void ClearMinimap()
    {
        if (minimap == null) return;

        if (_createdMinimap)
        {
            minimap.Clear();
            Destroy(minimap);
            _createdMinimap = false;
        }
        else
        {
            minimap.Clear();
        }

        minimap = null;
    }

    protected override void OnDestroy()
    {
        Unbind();
        base.OnDestroy();
    }
}
