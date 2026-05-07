using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

public interface IPool : IDisposable
{
    void ClearInactive();
    UniTask PrewarmAsync(int count, CancellationToken ct = default);
    void Return(Component comp);
}

public sealed class Pool<T> : IPool where T : Component
{
    private readonly T _prefab;
    private readonly Transform _root;
    private readonly IObjectPool<T> _pool;
    private readonly HashSet<T> _allMembers = new();
    private readonly Dictionary<T, IPoolable>   _poolableCache   = new();
    private readonly Dictionary<T, IAfterSpawn> _afterSpawnCache = new();
    private readonly Action<T> _onDestroyCallback;
    private bool _isPrewarming;

    public Pool(string address, T prefab, Transform parent, Action<T> onDestroyCallback, int initialSize, int maxSize)
    {
        _prefab = prefab;
        _onDestroyCallback = onDestroyCallback;
        _root = new GameObject($"Pool_{address}_{typeof(T).Name}").transform;
        _root.SetParent(parent, false);

        _pool = new ObjectPool<T>(
            createFunc:      CreateFunc,
            actionOnGet:     OnGet,
            actionOnRelease: OnRelease,
            actionOnDestroy: OnDestroyFunc,
            collectionCheck: false,
            defaultCapacity: initialSize,
            maxSize:         maxSize
        );
    }

    private T CreateFunc()
    {
        T inst = Object.Instantiate(_prefab, _root, false);
        inst.gameObject.SetActive(false);
        _allMembers.Add(inst);
        if (inst is IPoolable p)    _poolableCache[inst]   = p;
        if (inst is IAfterSpawn a)  _afterSpawnCache[inst] = a;
        return inst;
    }

    private void OnGet(T comp)
    {
        comp.gameObject.SetActive(true);
        if (!_isPrewarming && _poolableCache.TryGetValue(comp, out var p))
            p.OnSpawn();
    }

    private void OnRelease(T comp)
    {
        if (!_isPrewarming && _poolableCache.TryGetValue(comp, out var p))
            p.OnDespawn();
        comp.gameObject.SetActive(false);
        comp.transform.SetParent(_root, false);
    }

    private void OnDestroyFunc(T comp)
    {
        _poolableCache.Remove(comp);
        _afterSpawnCache.Remove(comp);
        _allMembers.Remove(comp);
        _onDestroyCallback?.Invoke(comp);
        if (comp != null) Object.Destroy(comp.gameObject);
    }

    public T Get(Transform parent)
    {
        Debug.Assert(PlayerLoopHelper.IsMainThread);
        T comp = _pool.Get();
        if (parent != null) comp.transform.SetParent(parent, false);
        return comp;
    }

    public void Release(T comp)
    {
        Debug.Assert(PlayerLoopHelper.IsMainThread);
        _pool.Release(comp);
    }

    public void Return(Component comp)
    {
        if (comp is T typedComp) Release(typedComp);
        else Debug.LogError($"[Pool] Type mismatch: Expected {typeof(T).Name}, but got {comp.GetType().Name}");
    }

    public async UniTask PrewarmAsync(int count, CancellationToken ct = default)
    {
        Debug.Assert(PlayerLoopHelper.IsMainThread);
        _isPrewarming = true;
        try
        {
            T[] temp = new T[count];
            for (int i = 0; i < count; i++)
            {
                temp[i] = _pool.Get();
                if (i > 0 && i % 5 == 0)
                    await UniTask.Yield(ct);
            }
            for (int i = 0; i < count; i++)
                _pool.Release(temp[i]);
        }
        finally
        {
            _isPrewarming = false;
        }
    }

    public void ClearInactive()
    {
        Debug.Assert(PlayerLoopHelper.IsMainThread);
        _pool.Clear();
    }

    public bool TryAfterSpawn(T comp)
    {
        if (_afterSpawnCache.TryGetValue(comp, out var a)) { a.AfterSpawn(); return true; }
        return false;
    }

    public void Dispose()
    {
        foreach (var comp in _allMembers)
        {
            if (comp == null) continue;
            _onDestroyCallback?.Invoke(comp);
            Object.Destroy(comp.gameObject);
        }
        _allMembers.Clear();
        _poolableCache.Clear();
        _afterSpawnCache.Clear();
        if (_root != null) Object.Destroy(_root.gameObject);
    }
}
