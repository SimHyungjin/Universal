using Cysharp.Threading.Tasks;
using System;

public class UI_Popup : UI_Panel
{
    public UIAnimationType AnimationType = UIAnimationType.None;
    public Action OnCloseRequest;

    public override void Open()
    {
        base.Open();
        this.StopAnimation();
        this.PlayIn(AnimationType).Forget();
    }

    public override void Close()
    {
        if (!_isOpened) return;
        _isOpened = false;
        CloseTask().Forget();
    }

    private async UniTask CloseTask()
    {
        this.StopAnimation();
        await this.PlayOut(AnimationType);
        OnCloseEvent?.Invoke();
        OnCloseRequest?.Invoke();
    }
}
