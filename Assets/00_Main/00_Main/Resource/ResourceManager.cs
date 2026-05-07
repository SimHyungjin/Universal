using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

public enum AssetCacheType { Required, NonRequired }

public class ResourceManager : PrimaryManager
{
    #region Constants

    private const string Tag = "[ResourceManager]";

    #endregion

    #region Fields

    private readonly Dictionary<string, AsyncOperationHandle> _requiredCache    = new();
    private readonly Dictionary<string, AsyncOperationHandle> _nonRequiredCache = new();
    private readonly Dictionary<string, AsyncOperationHandle> _loadingCache     = new();
    private readonly Dictionary<string, AssetCacheType>       _desiredType      = new();
    private readonly Dictionary<string, List<string>>         _labelToKeys      = new();
    private readonly HashSet<string>                          _logged           = new();

    #endregion

    #region Initialization

    protected override async UniTask OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        await Addressables.InitializeAsync().ToUniTask();
    }

    #endregion

    #region Load Asset

    public async UniTask<AsyncOperationHandle> LoadHandleAsync(
        string key,
        AssetCacheType cacheType = AssetCacheType.NonRequired,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(key)) return default;

        MarkDesiredType(key, cacheType);

        if (TryGetValidHandle(key, out var cachedHandle)) return cachedHandle;

        if (_loadingCache.TryGetValue(key, out var hLoading))
        {
            if (hLoading.IsValid())
            {
                await hLoading.ToUniTask(cancellationToken: ct);

                if (_desiredType.TryGetValue(key, out var want) && want == AssetCacheType.Required)
                    PromoteToRequiredIfNeeded(key);

                if (TryGetValidHandle(key, out var finishedHandle)) return finishedHandle;
                return default;
            }
            _loadingCache.Remove(key);
        }

        var handle = Addressables.LoadAssetAsync<object>(key);
        _loadingCache[key] = handle;

        try
        {
            await handle.ToUniTask(cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            if (handle.IsValid()) Addressables.Release(handle);
            _loadingCache.Remove(key);
            CleanupDesiredType();
            throw;
        }
        catch (Exception e)
        {
            if (handle.IsValid()) Addressables.Release(handle);
            _loadingCache.Remove(key);
            LogOnce($"handle_fail|{key}", $"{Tag} Load Fail key='{key}': {e.Message}");
            CleanupDesiredType();
            return default;
        }

        _loadingCache.Remove(key);

        if (!handle.IsValid() || handle.Status != AsyncOperationStatus.Succeeded)
        {
#if UNITY_EDITOR
            Debug.LogError(!IsKeyValid(key)
                ? $"{Tag} <color=red>키 오류:</color> '{key}'를 찾을 수 없습니다. (오타 또는 그룹 등록 누락)"
                : $"{Tag} 로드 실패: '{key}'의 상태가 {handle.Status}입니다.");
#endif
            if (handle.IsValid()) Addressables.Release(handle);
            CleanupDesiredType();
            return default;
        }

        var finalType = _desiredType.GetValueOrDefault(key, cacheType);

        if (finalType == AssetCacheType.Required)
            PromoteToRequiredIfNeeded(key);

        if (TryGetValidHandle(key, out var alreadyCached))
        {
            if (handle.IsValid() && !alreadyCached.Equals(handle))
                Addressables.Release(handle);

            CleanupDesiredType();
            return alreadyCached;
        }

        var cache = GetCache(finalType);
        if (cache.TryGetValue(key, out var existingInSameCache) && existingInSameCache.IsValid())
        {
            if (handle.IsValid() && !existingInSameCache.Equals(handle))
                Addressables.Release(handle);

            CleanupDesiredType();
            return existingInSameCache;
        }

        cache[key] = handle;
        CleanupDesiredType();
        return handle;
    }

    public async UniTask<T> LoadAssetAsync<T>(
        string key = null,
        AssetCacheType cacheType = AssetCacheType.NonRequired,
        CancellationToken ct = default) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key)) key = typeof(T).Name;

        var handle = await LoadHandleAsync(key, cacheType, ct);
        if (!handle.IsValid()) return null;

        if (handle.Result is T result) return result;

        if (typeof(Component).IsAssignableFrom(typeof(T)) && handle.Result is GameObject go)
        {
            if (go.TryGetComponent<T>(out var component)) return component;
        }

        LogOnce($"type_mismatch|{key}",
            $"{Tag} Type Mismatch: Key='{key}' Expected='{typeof(T).Name}' Actual='{handle.Result?.GetType().Name}'");
        return null;
    }

    public T GetAssetNow<T>(string key) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (TryGetValidHandle(key, out var handle) && handle.Result is T result) return result;
        return null;
    }

    public async UniTask<List<T>> LoadAssetsByLabelAsync<T>(
        string label,
        AssetCacheType cacheType = AssetCacheType.NonRequired,
        CancellationToken ct = default) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(label)) return new List<T>();

        var locHandle = Addressables.LoadResourceLocationsAsync(label, typeof(T));
        IList<IResourceLocation> locations;
        try
        {
            locations = await locHandle.ToUniTask(cancellationToken: ct);
        }
        catch (OperationCanceledException) { throw; }
        catch { return new List<T>(); }
        finally
        {
            if (locHandle.IsValid()) Addressables.Release(locHandle);
        }

        if (locations == null || locations.Count == 0) return new List<T>();

        if (!_labelToKeys.TryGetValue(label, out var groupKeys))
        {
            groupKeys = new List<string>();
            _labelToKeys[label] = groupKeys;
        }

        var tasks = new List<UniTask<T>>(locations.Count);
        foreach (var loc in locations)
        {
            if (!groupKeys.Contains(loc.PrimaryKey)) groupKeys.Add(loc.PrimaryKey);
            tasks.Add(LoadAssetAsync<T>(loc.PrimaryKey, cacheType, ct));
        }

        var results = await UniTask.WhenAll(tasks);
        var valid = new List<T>(results.Length);
        foreach (var item in results)
            if (item != null) valid.Add(item);

        return valid;
    }

    #endregion

    #region Release

    public void Release(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (ReleaseFromCache(_nonRequiredCache, key)) { _desiredType.Remove(key); return; }
        if (ReleaseFromCache(_requiredCache, key))    { _desiredType.Remove(key); return; }
        _desiredType.Remove(key);
        _loadingCache.Remove(key);
    }

    public void ReleaseLabel(string label)
    {
        if (string.IsNullOrEmpty(label) || !_labelToKeys.TryGetValue(label, out var keys)) return;
        var snapshot = new List<string>(keys);
        foreach (var key in snapshot) Release(key);
        _labelToKeys.Remove(label);
        CleanupDesiredType();
    }

    public void ClearNonRequired()
    {
        ReleaseAllInDictionary(_nonRequiredCache);
        CleanupLabelCache();
        CleanupDesiredType();
    }

    public void ClearRequired()
    {
        ReleaseAllInDictionary(_requiredCache);
        _labelToKeys.Clear();
        CleanupDesiredType();
    }

    public void ClearAll()
    {
        ClearNonRequired();
        ClearRequired();
        ClearLoading();
        _desiredType.Clear();
        _logged.Clear();
    }

    public override void Clear()
    {
        ClearNonRequired();
        _logged.Clear();
    }

    #endregion

    #region Internal Methods

    private Dictionary<string, AsyncOperationHandle> GetCache(AssetCacheType cacheType)
        => cacheType == AssetCacheType.Required ? _requiredCache : _nonRequiredCache;

    private static AssetCacheType MergeType(AssetCacheType a, AssetCacheType b)
        => (a == AssetCacheType.Required || b == AssetCacheType.Required)
            ? AssetCacheType.Required : AssetCacheType.NonRequired;

    private void MarkDesiredType(string key, AssetCacheType requested)
    {
        if (_desiredType.TryGetValue(key, out var prev))
            _desiredType[key] = MergeType(prev, requested);
        else
            _desiredType[key] = requested;

        if (_desiredType[key] == AssetCacheType.Required)
            PromoteToRequiredIfNeeded(key);
    }

    private bool TryGetValidHandle(string key, out AsyncOperationHandle handle)
    {
        if (_requiredCache.TryGetValue(key, out handle))
        {
            if (handle.IsValid()) return true;
            _requiredCache.Remove(key);
        }
        if (_nonRequiredCache.TryGetValue(key, out handle))
        {
            if (handle.IsValid()) return true;
            _nonRequiredCache.Remove(key);
        }
        handle = default;
        return false;
    }

    private void PromoteToRequiredIfNeeded(string key)
    {
        if (_nonRequiredCache.TryGetValue(key, out var h))
        {
            if (h.IsValid()) { _nonRequiredCache.Remove(key); _requiredCache[key] = h; }
            else               _nonRequiredCache.Remove(key);
        }
    }

    private bool ReleaseFromCache(Dictionary<string, AsyncOperationHandle> cache, string key)
    {
        if (!cache.TryGetValue(key, out var handle)) return false;
        if (handle.IsValid()) Addressables.Release(handle);
        cache.Remove(key);
        return true;
    }

    private void ReleaseAllInDictionary(Dictionary<string, AsyncOperationHandle> dict)
    {
        var handles = new List<AsyncOperationHandle>(dict.Values);
        dict.Clear();
        foreach (var h in handles)
            if (h.IsValid()) Addressables.Release(h);
    }

    private void ClearLoading()
    {
        var handles = new List<AsyncOperationHandle>(_loadingCache.Values);
        _loadingCache.Clear();
        foreach (var h in handles)
            if (h.IsValid()) Addressables.Release(h);
        CleanupDesiredType();
    }

    private void CleanupLabelCache()
    {
        var labels = new List<string>(_labelToKeys.Keys);
        foreach (var label in labels)
        {
            var keys = _labelToKeys[label];
            keys.RemoveAll(key => !_requiredCache.ContainsKey(key) && !_nonRequiredCache.ContainsKey(key));
            if (keys.Count == 0) _labelToKeys.Remove(label);
        }
    }

    private void CleanupDesiredType()
    {
        if (_desiredType.Count == 0) return;
        var keys = new List<string>(_desiredType.Keys);
        foreach (var key in keys)
        {
            if (!_requiredCache.ContainsKey(key) &&
                !_nonRequiredCache.ContainsKey(key) &&
                !_loadingCache.ContainsKey(key))
                _desiredType.Remove(key);
        }
    }

    private void LogOnce(string signature, string message)
    {
        if (_logged.Add(signature)) Debug.LogWarning(message);
    }

    public bool IsKeyValid(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        foreach (var locator in Addressables.ResourceLocators)
            if (locator.Locate(key, typeof(object), out _)) return true;
        return false;
    }

    #endregion
}
