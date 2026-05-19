using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class UIManager_HudLayer
{
    private readonly Transform _root;
    private readonly int       _order;
    private UI_Hud _current;

    public UIManager_HudLayer(Transform root, int order)
    {
        _root  = root;
        _order = order;
    }

    public async UniTask<T> Show<T>(string key, CancellationToken ct) where T : UI_Hud
    {
        Close();
        var instance = await Main.Pool.SpawnAsync<T>(key ?? typeof(T).Name, _root, null, ct);
        if (instance == null) return null;

        if (instance.TryGetComponent(out Canvas canvas))
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder    = _order;
            instance.SetupAsNestedCanvas();
        }

        instance.Open();
        _current = instance;
        return instance;
    }

    public void Close()
    {
        if (_current == null) return;
        var target = _current;
        _current = null;
        target.Close();
        Main.Pool.Despawn(target);
    }
}
