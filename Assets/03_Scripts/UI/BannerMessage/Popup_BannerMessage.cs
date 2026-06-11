using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Michsky.UI.MTP;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Popup_BannerMessage : UI_Popup, IPoolable
{
    [Header("Motion Title")]
    [SerializeField] private StyleManager _styleManager;
    [SerializeField] private int _textItemIndex = -1;

    [Header("Default")]
    [SerializeField] private string _defaultMessage = "";
    [SerializeField, Min(0f)] private float _defaultDuration = 2f;
    [SerializeField, Range(0.1f, 2.5f)] private float _animationSpeed = 1f;
    [SerializeField] private bool _useUnscaledTime = true;
    [SerializeField] private bool _disableRaycastTargets = true;

    private CancellationTokenSource _autoCloseCts;
    private CancellationTokenSource _closeCts;
    private bool _isClosing;

    public bool IsShowing => _isOpened && !_isClosing;

    protected override void Awake()
    {
        base.Awake();
        CacheReferences();
        ConfigureStyleManager();
    }

    public override bool Initialize()
    {
        bool initialized = base.Initialize();
        CacheReferences();
        ConfigureStyleManager();
        return initialized;
    }

    public override void Open()
    {
        _isClosing = false;
        base.Open();

        if (!string.IsNullOrEmpty(_defaultMessage))
            Show(_defaultMessage, _defaultDuration);
    }

    public override void Close()
    {
        if (!_isOpened || _isClosing) return;

        _isOpened = false;
        _isClosing = true;
        CloseAsync().Forget();
    }

    public void OnSpawn()
    {
        _isClosing = false;
        CacheReferences();
        ConfigureStyleManager();

        if (_styleManager != null)
            _styleManager.gameObject.SetActive(true);
    }

    public void OnDespawn()
    {
        CancelAutoClose();
        CancelClose();
        _isClosing = false;
    }

    public void Show(string message, float duration = -1f)
    {
        CacheReferences();
        ConfigureStyleManager();
        CancelAutoClose();
        CancelClose();

        if (!_isOpened)
            base.Open();

        _isClosing = false;
        ApplyMessage(message);
        PlayInAnimation();

        float resolvedDuration = duration >= 0f ? duration : _defaultDuration;
        _autoCloseCts = new CancellationTokenSource();
        AutoCloseAsync(resolvedDuration, _autoCloseCts.Token).Forget();
    }

    public void SetMessage(string message)
    {
        CacheReferences();
        ApplyMessage(message);
    }

    public void SetInfo(string message, float duration = -1f)
    {
        Show(message, duration);
    }

    public static async UniTask<Popup_BannerMessage> ShowAsync(
        string message,
        float duration = -1f,
        CancellationToken ct = default)
    {
        Popup_BannerMessage popup = await App.ShowPopup<Popup_BannerMessage>(
            clickGuard: false,
            clickGuardAlpha: 0f,
            clickClose: false,
            ct: ct);

        if (popup != null)
            popup.Show(message, duration);

        return popup;
    }

    private void CacheReferences()
    {
        if (_styleManager == null)
            _styleManager = GetComponentInChildren<StyleManager>(true);
    }

    private void ConfigureStyleManager()
    {
        if (_styleManager == null) return;

        _styleManager.playOnEnable = false;
        _styleManager.playOutAnimation = false;
        _styleManager.disableOnOut = false;
        _styleManager.loopAnimations = false;
        _styleManager.UseUnscaledTime = _useUnscaledTime;
        _styleManager.InitializeSpeed(_animationSpeed);

        if (_disableRaycastTargets)
            DisableRaycastTargets();
    }

    private void ApplyMessage(string message)
    {
        if (_styleManager == null || _styleManager.textItems == null) return;

        if (_textItemIndex >= 0)
        {
            if (_textItemIndex < _styleManager.textItems.Count)
                ApplyText(_styleManager.textItems[_textItemIndex], message);
            return;
        }

        foreach (TextItem item in _styleManager.textItems)
            ApplyText(item, message);
    }

    private static void ApplyText(TextItem item, string message)
    {
        if (item == null) return;

        if (item.textObject == null)
            item.textObject = item.GetComponent<TextMeshProUGUI>();

        item.text = message;
        item.UpdateAll();
    }

    private void PlayInAnimation()
    {
        if (_styleManager == null) return;

        if (!_styleManager.gameObject.activeSelf)
            _styleManager.gameObject.SetActive(true);

        _styleManager.PlayIn();
    }

    private void DisableRaycastTargets()
    {
        Graphic[] graphics = GetComponentsInChildren<Graphic>(true);
        foreach (Graphic graphic in graphics)
            graphic.raycastTarget = false;
    }

    private async UniTaskVoid AutoCloseAsync(float duration, CancellationToken ct)
    {
        try
        {
            if (duration > 0f)
                await UniTask.Delay(TimeSpan.FromSeconds(duration), DelayType.Realtime, cancellationToken: ct);

            if (ct.IsCancellationRequested || !IsShowing) return;

            if (Main.UI != null) App.ClosePopup(this);
            else Close();
        }
        catch (OperationCanceledException) { }
    }

    private async UniTaskVoid CloseAsync()
    {
        CancelAutoClose();
        CancelClose();
        _closeCts = new CancellationTokenSource();

        try
        {
            await PlayOutAnimationAsync(_closeCts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        OnCloseEvent?.Invoke();
        OnCloseRequest?.Invoke();
    }

    private async UniTask PlayOutAnimationAsync(CancellationToken ct)
    {
        if (_styleManager == null || !_styleManager.gameObject.activeInHierarchy)
        {
            await this.PlayOut(AnimationType);
            return;
        }

        _styleManager.PlayOut();

        float speed = Mathf.Max(0.01f, _animationSpeed);
        float timeout = _styleManager.outAnim != null ? (_styleManager.outAnim.length / speed) + 0.25f : 1f;
        float elapsed = 0f;

        while (_styleManager != null && _styleManager.IsPlaying && elapsed < timeout)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            elapsed += _useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        }
    }

    private void CancelAutoClose()
    {
        _autoCloseCts?.Cancel();
        _autoCloseCts?.Dispose();
        _autoCloseCts = null;
    }

    private void CancelClose()
    {
        _closeCts?.Cancel();
        _closeCts?.Dispose();
        _closeCts = null;
    }

    protected override void OnDestroy()
    {
        CancelAutoClose();
        CancelClose();
        base.OnDestroy();
    }
}
