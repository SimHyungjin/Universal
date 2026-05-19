using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PrimeTween;
using UnityEngine;

public enum UIAnimationType
{
    None,
    Scale,
    Fade,
    SlideFromTop,
    SlideFromBottom,
    SlideFromLeft,
    SlideFromRight
}

public static class UI_Animation
{
    public const float ANIM_DURATION = 0.30f;
    private static readonly Dictionary<Transform, Tween> _activeTweens = new();

    private static Vector2 GetCanvasSize(Transform tr)
    {
        var canvas = tr.GetComponentInParent<Canvas>(true);
        if (canvas == null) return new Vector2(Screen.width, Screen.height);
        var rt = canvas.rootCanvas.GetComponent<RectTransform>();
        return rt != null ? rt.rect.size : new Vector2(Screen.width, Screen.height);
    }

    public static async UniTask PlayIn(this UI ui, UIAnimationType type)
    {
        if (ui == null) return;
        Transform tr = ui.transform;
        StopTween(tr);

        switch (type)
        {
            case UIAnimationType.Scale:
                tr.localScale = Vector3.zero;
                await PlayTween(tr, Tween.Scale(tr, Vector3.one, ANIM_DURATION, Ease.OutBack, useUnscaledTime: true));
                break;

            case UIAnimationType.Fade:
                if (tr.TryGetComponent(out CanvasGroup group))
                {
                    group.alpha = 0f;
                    await PlayTween(tr, Tween.Alpha(group, 1f, ANIM_DURATION, useUnscaledTime: true));
                }
                break;

            case UIAnimationType.SlideFromTop:
            case UIAnimationType.SlideFromBottom:
            case UIAnimationType.SlideFromLeft:
            case UIAnimationType.SlideFromRight:
                Vector2 sIn = GetCanvasSize(tr);
                if (type == UIAnimationType.SlideFromTop)    await PlaySlide(tr, new Vector3(0,   sIn.y, 0), Vector3.zero, Ease.OutQuad);
                if (type == UIAnimationType.SlideFromBottom) await PlaySlide(tr, new Vector3(0,  -sIn.y, 0), Vector3.zero, Ease.OutQuad);
                if (type == UIAnimationType.SlideFromLeft)   await PlaySlide(tr, new Vector3(-sIn.x,  0, 0), Vector3.zero, Ease.OutQuad);
                if (type == UIAnimationType.SlideFromRight)  await PlaySlide(tr, new Vector3( sIn.x,  0, 0), Vector3.zero, Ease.OutQuad);
                break;

            default:
                tr.localScale = Vector3.one;
                break;
        }
    }

    public static async UniTask PlayOut(this UI ui, UIAnimationType type)
    {
        if (ui == null) return;
        Transform tr = ui.transform;
        StopTween(tr);

        switch (type)
        {
            case UIAnimationType.Scale:
                await PlayTween(tr, Tween.Scale(tr, Vector3.zero, ANIM_DURATION, Ease.InBack, useUnscaledTime: true));
                break;

            case UIAnimationType.Fade:
                if (tr.TryGetComponent(out CanvasGroup group))
                    await PlayTween(tr, Tween.Alpha(group, 0f, ANIM_DURATION, useUnscaledTime: true));
                break;

            case UIAnimationType.SlideFromTop:
            case UIAnimationType.SlideFromBottom:
            case UIAnimationType.SlideFromLeft:
            case UIAnimationType.SlideFromRight:
                Vector2 sOut = GetCanvasSize(tr);
                if (type == UIAnimationType.SlideFromTop)    await PlaySlide(tr, Vector3.zero, new Vector3(0,   sOut.y, 0), Ease.InQuad);
                if (type == UIAnimationType.SlideFromBottom) await PlaySlide(tr, Vector3.zero, new Vector3(0,  -sOut.y, 0), Ease.InQuad);
                if (type == UIAnimationType.SlideFromLeft)   await PlaySlide(tr, Vector3.zero, new Vector3(-sOut.x,  0, 0), Ease.InQuad);
                if (type == UIAnimationType.SlideFromRight)  await PlaySlide(tr, Vector3.zero, new Vector3( sOut.x,  0, 0), Ease.InQuad);
                break;
        }
    }

    private static async UniTask PlaySlide(Transform tr, Vector3 start, Vector3 end, Ease ease)
    {
        tr.localPosition = start;
        await PlayTween(tr, Tween.LocalPosition(tr, end, ANIM_DURATION, ease, useUnscaledTime: true));
    }

    public static void StopAnimation(this UI ui)
    {
        if (ui == null) return;
        StopTween(ui.transform);
    }

    private static async UniTask PlayTween(Transform tr, Tween tween)
    {
        _activeTweens[tr] = tween;
        try { await tween; }
        finally { _activeTweens.Remove(tr); }
    }

    private static void StopTween(Transform tr)
    {
        if (tr == null) return;
        if (!_activeTweens.TryGetValue(tr, out Tween tween)) return;

        if (tween.isAlive) tween.Stop();
        _activeTweens.Remove(tr);
    }
}
