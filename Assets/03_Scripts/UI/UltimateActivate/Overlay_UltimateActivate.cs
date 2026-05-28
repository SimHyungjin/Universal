using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class Overlay_UltimateActivate : UI_Overlay
{
    public static Overlay_UltimateActivate Instance { get; private set; }

    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image       flashImage;
    [SerializeField] private Image       portraitImage;

    private CancellationTokenSource _cts;

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
        SetAlpha(0f);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (Instance == this) Instance = null;
        _cts?.Cancel();
        _cts?.Dispose();
    }

    public void Play(UltimateOverlayData data)
    {
        if (!data.enabled) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        PlayAsync(data, _cts.Token).Forget();
    }

    private async UniTaskVoid PlayAsync(UltimateOverlayData data, CancellationToken ct)
    {
        try
        {
            if (flashImage != null)    flashImage.color      = data.flashColor;
            if (portraitImage != null)
            {
                portraitImage.sprite  = data.portrait;
                portraitImage.enabled = data.portrait != null;
            }

            _isOpened = true;

            await FadeAsync(0f, 1f, data.fadeInDuration, ct);

            if (data.holdDuration > 0f)
                await UniTask.Delay(TimeSpan.FromSeconds(data.holdDuration), ignoreTimeScale: true, cancellationToken: ct);

            await FadeAsync(1f, 0f, data.fadeOutDuration, ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            SetAlpha(0f);
            _isOpened = false;
        }
    }

    private async UniTask FadeAsync(float from, float to, float duration, CancellationToken ct)
    {
        if (canvasGroup == null) return;
        if (duration <= 0f) { SetAlpha(to); return; }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed           += Time.unscaledDeltaTime;
            canvasGroup.alpha  = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        SetAlpha(to);
    }

    private void SetAlpha(float alpha)
    {
        if (canvasGroup != null) canvasGroup.alpha = alpha;
    }
}
