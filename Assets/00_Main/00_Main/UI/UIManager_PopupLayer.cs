using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class UIManager_PopupLayer
{
    private class PopupData
    {
        public UI_Popup Popup;
        public Canvas   Canvas;
        public bool     IsClickGuard;
        public float    ClickGuardAlpha;
        public bool     ClickClose;
        public bool     IsClosing;
        public CancellationTokenSource TimeoutCts;

        public PopupData(UI_Popup popup, Canvas canvas, bool isClickGuard, float alpha, bool clickClose)
        {
            Popup           = popup;
            Canvas          = canvas;
            IsClickGuard    = isClickGuard;
            ClickGuardAlpha = alpha;
            ClickClose      = clickClose;
            IsClosing       = false;
        }
    }

    private const float GUARD_DURATION = 0.2f;
    private readonly int       _startOrder;
    private readonly Transform _root;
    private readonly List<PopupData> _popups = new();

    private Canvas _panelCanvas;
    private Image  _panelImage;
    private Tween  _panelTween;

    public UIManager_PopupLayer(Transform root, int order)
    {
        _root       = root;
        _startOrder = order;
        InitBackgroundPanel();
    }

    private void InitBackgroundPanel()
    {
        var go = new GameObject("[GlobalPopupPanel]", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        go.transform.SetParent(_root, false);

        _panelCanvas = go.GetComponent<Canvas>();
        _panelCanvas.renderMode     = RenderMode.ScreenSpaceOverlay;
        _panelCanvas.overrideSorting = true;

        var panelGo = new GameObject("Image", typeof(RectTransform));
        panelGo.transform.SetParent(go.transform, false);
        _panelImage = panelGo.AddComponent<Image>();
        _panelImage.color = new Color(0, 0, 0, 0);

        var rt = _panelImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        var btn = panelGo.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(OnClickPanel);

        go.SetActive(false);
    }

    public async UniTask<T> Show<T>(
        string key,
        bool clickGuard,
        float clickGuardAlpha,
        bool clickToClose,
        CancellationToken ct) where T : UI_Popup
    {
        T instance = await Main.Pool.SpawnAsync<T>(key ?? typeof(T).Name, _root, null, ct);
        if (instance == null) return null;

        Canvas pCanvas = instance.GetComponent<Canvas>();
        if (pCanvas != null)
        {
            pCanvas.overrideSorting = true;
            instance.SetupAsNestedCanvas();
        }

        instance.Open();
        instance.OnCloseRequest = () => Close(instance);

        float alpha = clickGuardAlpha < 0 ? 0.6f : clickGuardAlpha;
        _popups.Add(new PopupData(instance, pCanvas, clickGuard, alpha, clickToClose));

        RefreshState();
        return instance;
    }

    public void RequestClose(UI_Popup popup)
    {
        var data = _popups.Find(p => p.Popup == popup);
        if (data == null || data.IsClosing) return;

        data.IsClosing = true;
        data.TimeoutCts = new CancellationTokenSource();
        StartCloseTimeout(popup, data.TimeoutCts.Token).Forget();
        popup.Close();
        RefreshState();
    }

    public void Close(UI_Popup popup)
    {
        int idx = _popups.FindIndex(p => p.Popup == popup);
        if (idx < 0) return;

        var data = _popups[idx];
        data.TimeoutCts?.Cancel();
        data.TimeoutCts?.Dispose();
        data.TimeoutCts = null;

        if (data.Popup != null) Main.Pool.Despawn(data.Popup);

        _popups.RemoveAt(idx);
        RefreshState();
    }

    private void RefreshState()
    {
        StopPanelTween();
        _popups.RemoveAll(p => p.Popup == null);

        int topActiveIdx = _popups.FindLastIndex(p => !p.IsClosing && p.IsClickGuard && p.Canvas != null);

        for (int i = 0; i < _popups.Count; i++)
            if (_popups[i].Canvas != null)
                _popups[i].Canvas.sortingOrder = _startOrder + (i * 10);

        if (topActiveIdx == -1)
        {
            _panelTween = Tween.Alpha(_panelImage, 0f, GUARD_DURATION, useUnscaledTime: true)
                .OnComplete(() =>
                {
                    if (_popups.FindLastIndex(p => !p.IsClosing && p.IsClickGuard) == -1)
                        _panelCanvas.gameObject.SetActive(false);
                });
        }
        else
        {
            var topData = _popups[topActiveIdx];
            _panelCanvas.gameObject.SetActive(true);
            _panelCanvas.sortingOrder = topData.Canvas.sortingOrder - 1;
            _panelTween = Tween.Alpha(_panelImage, topData.ClickGuardAlpha, GUARD_DURATION, useUnscaledTime: true);
        }
    }

    private void StopPanelTween()
    {
        if (_panelTween.isAlive) _panelTween.Stop();
    }

    private void OnClickPanel()
    {
        int idx = _popups.FindLastIndex(p => !p.IsClosing && p.IsClickGuard && p.ClickClose);
        if (idx >= 0) RequestClose(_popups[idx].Popup);
    }

    public void CloseTop(bool withAnimation = true)
    {
        int idx = _popups.FindLastIndex(p => !p.IsClosing);
        if (idx < 0) return;

        if (withAnimation) RequestClose(_popups[idx].Popup);
        else               Close(_popups[idx].Popup);
    }

    public void ClearAll(bool instant = false)
    {
        for (int i = _popups.Count - 1; i >= 0; i--)
        {
            var data = _popups[i];
            if (data.IsClosing)
            {
                if (instant) Close(data.Popup);
                continue;
            }
            if (instant) Close(data.Popup);
            else         RequestClose(data.Popup);
        }
    }

    private async UniTaskVoid StartCloseTimeout(UI_Popup popup, CancellationToken ct)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(3f), delayType: DelayType.Realtime, cancellationToken: ct);
            if (_popups.Exists(p => p.Popup == popup)) Close(popup);
        }
        catch (OperationCanceledException) { }
    }
}
