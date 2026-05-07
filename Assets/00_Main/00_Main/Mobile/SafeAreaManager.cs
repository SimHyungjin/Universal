using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class SafeAreaManager : CoreManager
{
    public struct SafeAreaData
    {
        public Vector2 AnchorMin;
        public Vector2 AnchorMax;
        public float Left, Right, Top, Bottom;
    }

    public event Action<SafeAreaData> OnSafeAreaChanged;
    private SafeAreaData _currentData;

    protected override async UniTask OnInitializeAsync()
    {
        UpdateSafeArea();
        await UniTask.CompletedTask;
    }

    public void UpdateSafeArea()
    {
        Rect safeArea = Screen.safeArea;
        float w = Screen.width;
        float h = Screen.height;

        Vector2 min = safeArea.position;
        Vector2 max = safeArea.position + safeArea.size;

        min.x /= w; min.y /= h;
        max.x /= w; max.y /= h;

        if (w > h) { min.y = 0f; max.y = 1f; }
        else       { min.x = 0f; max.x = 1f; }

        _currentData = new SafeAreaData
        {
            AnchorMin = min,
            AnchorMax = max,
            Left   = min.x,
            Right  = max.x,
            Top    = max.y,
            Bottom = min.y,
        };

        OnSafeAreaChanged?.Invoke(_currentData);
    }

    public SafeAreaData GetCurrentData() => _currentData;
}
