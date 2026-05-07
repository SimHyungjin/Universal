using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

#region Interfaces

public interface IPoolable
{
    void OnSpawn();
    void OnDespawn();
}

public interface IAfterSpawn
{
    void AfterSpawn();
}

#endregion

public struct PoolConfig
{
    public int InitialSize;
    public int MaxSize;
    public static PoolConfig Default => new() { InitialSize = 1, MaxSize = 1000 };
}

public sealed class PoolManager : CoreManager
{
    private readonly Dictionary<string, IPool>       _pools             = new();
    private readonly Dictionary<Component, string>   _instanceToAddress = new();
    private readonly Dictionary<GameObject, Component> _goToComponent   = new();
    private readonly Dictionary<string, UniTask>     _loadingTasks      = new();
    private GameObject _root;

    public async UniTask<T> SpawnAsync<T>(
        string address = null,
        Transform parent = null,
        PoolConfig? config = null,
        CancellationToken token = default) where T : Component
    {
        string key = address ?? typeof(T).Name;

        if (!_pools.ContainsKey(key))
            await LoadInternal<T>(key, config ?? PoolConfig.Default, token);

        if (!_pools.TryGetValue(key, out var loadedPool))
        {
            Debug.LogError($"[PoolManager] Spawn failed: '{key}' pool was not created.");
            return null;
        }

        if (loadedPool is not Pool<T> pool)
        {
            Debug.LogError($"[PoolManager] Type mismatch: {key}");
            return null;
        }

        await UniTask.SwitchToMainThread(token);

        T comp = pool.Get(parent);
        _instanceToAddress[comp] = key;
        _goToComponent[comp.gameObject] = comp;
        pool.TryAfterSpawn(comp);

        return comp;
    }

    public void Despawn(Component comp)
    {
        if (comp == null) return;
        Debug.Assert(PlayerLoopHelper.IsMainThread);

        _goToComponent.Remove(comp.gameObject);

        if (_instanceToAddress.Remove(comp, out string address))
        {
            if (_pools.TryGetValue(address, out var pool))
            {
                pool.Return(comp);
                return;
            }
        }
        Object.Destroy(comp.gameObject);
    }

    public void Despawn(GameObject go)
    {
        if (go == null) return;
        if (_goToComponent.TryGetValue(go, out var comp))
        {
            Despawn(comp);
            return;
        }
        Object.Destroy(go);
    }

    public void ClearAllInactive()
    {
        Debug.Assert(PlayerLoopHelper.IsMainThread);
        foreach (var pool in _pools.Values) pool.ClearInactive();
    }

    public void ClearPool(string address)
    {
        Debug.Assert(PlayerLoopHelper.IsMainThread);
        if (_pools.Remove(address, out var pool))
        {
            pool.Dispose();
            Main.Resource.Release(address);
        }
    }

    public override void Clear()
    {
        Debug.Assert(PlayerLoopHelper.IsMainThread);
        foreach (var pool in _pools.Values) pool.Dispose();
        _pools.Clear();
        _instanceToAddress.Clear();
        _goToComponent.Clear();
        _loadingTasks.Clear();
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
    }

    private void EnsureRoot()
    {
        if (_root == null)
        {
            _root = new GameObject("@Pool_Root");
            Object.DontDestroyOnLoad(_root);
        }
    }

    private async UniTask LoadInternal<T>(string address, PoolConfig config, CancellationToken token) where T : Component
    {
        if (_loadingTasks.TryGetValue(address, out var loadingTask))
        {
            await loadingTask;
            return;
        }

        var utcs = new UniTaskCompletionSource();
        _loadingTasks[address] = utcs.Task;

        try
        {
            T prefab = await Main.Resource.LoadAssetAsync<T>(address, ct: token);
            await UniTask.SwitchToMainThread(token);

            if (prefab == null)
            {
                Debug.LogError($"[PoolManager] Load failed: address='{address}', type='{typeof(T).Name}'");
                utcs.TrySetResult();
                return;
            }

            EnsureRoot();

            var pool = new Pool<T>(address, prefab, _root.transform,
                comp => _instanceToAddress.Remove(comp), config.InitialSize, config.MaxSize);
            _pools[address] = pool;

            if (config.InitialSize > 0)
                await pool.PrewarmAsync(config.InitialSize, token);

            utcs.TrySetResult();
        }
        catch (Exception e)
        {
            utcs.TrySetException(e);
            throw;
        }
        finally
        {
            _loadingTasks.Remove(address);
        }
    }
}
