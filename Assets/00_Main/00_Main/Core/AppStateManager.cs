using System;

public class AppStateManager : CoreManager
{
    private bool _isForeground = true;
    public bool IsWatchingAd { get; set; }

    public event Action OnAppStateForeground;
    public event Action OnAppStateBackground;

    public void HandleAppStateChange(bool isForeground)
    {
        if (IsWatchingAd || _isForeground == isForeground) return;

        _isForeground = isForeground;

        if (_isForeground)
            OnAppStateForeground?.Invoke();
        else
            OnAppStateBackground?.Invoke();
    }
}
