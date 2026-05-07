using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class UIManager_OverlayLayer
{
    private readonly Canvas          _canvas;
    private readonly List<UI_Overlay> _overlays = new();

    public UIManager_OverlayLayer(Canvas canvas) => _canvas = canvas;

    public async UniTask<T> Show<T>(string key, CancellationToken ct) where T : UI_Overlay
    {
        var instance = await Main.Pool.SpawnAsync<T>(key ?? typeof(T).Name, _canvas.transform, null, ct);
        if (instance == null) return null;

        _overlays.Add(instance);
        instance.Open();
        return instance;
    }

    public void Close(UI_Overlay instance)
    {
        if (instance == null) return;
        _overlays.Remove(instance);
        instance.Close();
        Main.Pool.Despawn(instance);
    }

    public void CloseAll()
    {
        for (int i = _overlays.Count - 1; i >= 0; i--)
            Close(_overlays[i]);
    }
}
