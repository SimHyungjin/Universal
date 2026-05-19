using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class UIManager : PrimaryManager
{
    private const int OVERLAY_ORDER = 0;
    private const int HUD_ORDER     = 1000;
    private const int POPUP_ORDER   = 2000;
    private const int SCENE_ORDER   = 10000;

    private UIManager_OverlayLayer _overlayLayer;
    private UIManager_HudLayer     _hudLayer;
    private UIManager_PopupLayer   _popupLayer;
    private UIManager_SceneLayer   _sceneLayer;

    protected override async UniTask OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        GameObject uiRoot = new("@UI");
        Object.DontDestroyOnLoad(uiRoot);
        uiRoot.transform.SetSiblingIndex(1);

        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var esPrefab = Resources.Load<UnityEngine.EventSystems.EventSystem>("EventSystem");
            if (esPrefab != null)
            {
                var esObj = Object.Instantiate(esPrefab);
                esObj.name = "[EventSystem]";
                Object.DontDestroyOnLoad(esObj);
            }
        }

        Canvas canvasPrefab = Resources.Load<Canvas>("Canvas");

        _overlayLayer = new UIManager_OverlayLayer(CreateCanvas(canvasPrefab, uiRoot.transform, "Overlay", OVERLAY_ORDER));
        _hudLayer     = new UIManager_HudLayer(CreateCanvas(canvasPrefab, uiRoot.transform, "Hud",   HUD_ORDER).transform,   HUD_ORDER);
        _popupLayer   = new UIManager_PopupLayer(CreateCanvas(canvasPrefab, uiRoot.transform, "Popup", POPUP_ORDER).transform, POPUP_ORDER);
        _sceneLayer   = new UIManager_SceneLayer(CreateCanvas(canvasPrefab, uiRoot.transform, "Scene", SCENE_ORDER).transform, SCENE_ORDER);
    }

    private Canvas CreateCanvas(Canvas prefab, Transform parent, string name, int order)
    {
        Canvas canvas = Object.Instantiate(prefab, parent);
        canvas.name = name;
        canvas.sortingOrder = order;
        return canvas;
    }

    public async UniTask<T> ShowHud<T>(string key = null, CancellationToken ct = default)     where T : UI_Hud     => await _hudLayer.Show<T>(key, ct);
    public async UniTask<T> ShowOverlay<T>(string key = null, CancellationToken ct = default) where T : UI_Overlay  => await _overlayLayer.Show<T>(key, ct);
    public async UniTask<T> ShowScene<T>(string key = null, CancellationToken ct = default)   where T : UI_Scene    => await _sceneLayer.Show<T>(key, ct);

    public async UniTask<T> ShowPopup<T>(
        string key = null,
        bool clickGuard = true,
        float clickGuardAlpha = -1f,
        bool clickToClose = true,
        CancellationToken ct = default) where T : UI_Popup
        => await _popupLayer.Show<T>(key, clickGuard, clickGuardAlpha, clickToClose, ct);

    public void CloseHud()                              => _hudLayer.Close();
    public void CloseOverlay(UI_Overlay overlay)        => _overlayLayer.Close(overlay);
    public void CloseAllOverlays()                      => _overlayLayer.CloseAll();
    public void ClosePopup(UI_Popup popup)              => _popupLayer.RequestClose(popup);
    public void CloseTopPopup(bool withAnimation = true) => _popupLayer.CloseTop(withAnimation);
    public void CloseAllPopups(bool instant = false)    => _popupLayer.ClearAll(instant);
    public void CloseScene()                            => _sceneLayer.Close();
}
