using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 자주 사용하는 매니저 접근을 단축하는 정적 헬퍼.
/// 게임 고유 데이터(재화, 진행도 등)가 필요하면 이 클래스를 확장하거나
/// 새 partial 파일로 분리해서 추가하세요.
/// </summary>
public static class App
{
    #region Data

    public static PlayerData Player => Main.Data?.Player;

    public static void SaveData()        => Main.Data?.SaveImmediate();
    public static void SaveDataIfDirty() => Main.Data?.SaveIfDirty();
    public static void MarkDataDirty()   => Main.Data?.MarkDirty();
    public static void ResetData()       => Main.Data?.ResetPlayer();

    public static void SetMasterVolume(float v) { Player.MasterVolume = v; Main.Audio?.SetVolume(AudioChannelType.Master, v); MarkDataDirty(); }
    public static void SetBgmVolume(float v)    { Player.BgmVolume    = v; Main.Audio?.SetVolume(AudioChannelType.Music,  v); MarkDataDirty(); }
    public static void SetSfxVolume(float v)    { Player.SfxVolume    = v; Main.Audio?.SetVolume(AudioChannelType.Sound,  v); MarkDataDirty(); }
    public static void SetLanguage(string v)    { Player.Language     = v; MarkDataDirty(); }

    #endregion

    #region Audio

    public static void PlayBgm(BgmType key, float crossfade = 1f) => Main.Audio?.PlayBgm(key, crossfade);
    public static void StopBgm(float fade = 0.5f)                => Main.Audio?.StopBgm(fade);
    public static void PlaySfx(SfxType key)                       => Main.Audio?.PlaySfx(key);
    public static void PlaySfx(SfxType key, Vector3 position)     => Main.Audio?.PlaySfx(key, position);

    public static float GetVolume(AudioChannelType channel)      => Main.Audio?.GetVolume(channel) ?? 1f;

    #endregion

    #region Input

    public static T SetInput<T>(Action<T> onInit) where T : InputActions
        => Main.Input.SetInput<T>(onInit);

    public static void SetInput<T>() where T : InputActions
        => Main.Input.SetInput<T>();

    public static void SetInput<T1, T2>()
        where T1 : InputActions
        where T2 : InputActions
        => Main.Input.SetInput<T1, T2>();

    public static void SetInput<T1, T2, T3>()
        where T1 : InputActions
        where T2 : InputActions
        where T3 : InputActions
        => Main.Input.SetInput<T1, T2, T3>();

    public static void AddInput<T>() where T : InputActions
        => Main.Input.AddInput<T>();

    public static void RemoveInput<T>() where T : InputActions
        => Main.Input.RemoveInput<T>();

    public static void RemoveAllInputs() => Main.Input.RemoveAllInputs();

    #endregion

    #region Camera

    public static void SetCameraFollow(
        Transform target,
        Vector3? offset = null,
        float followSpeed = 12f,
        bool snap = true)
        => Main.Camera?.SetFollowTarget(target, offset, followSpeed, snap);

    public static void SetCameraView(
        Vector3 position,
        Vector3 eulerAngles,
        bool orthographic = true,
        float orthographicSize = 8f)
        => Main.Camera?.SetView(position, eulerAngles, orthographic, orthographicSize);

    public static void ClearCameraFollow(Transform target)
        => Main.Camera?.ClearFollowTarget(target);

    public static void SetCombatCameraMode(CombatCameraMode mode, bool snap = false)
        => Main.Camera?.SetMode(mode, snap);

    public static void ToggleCombatCameraMode()
        => Main.Camera?.ToggleMode();

    #endregion

    #region Resource

    public static async UniTask<T> LoadAssetAsync<T>(
        string key = null,
        AssetCacheType cacheType = AssetCacheType.NonRequired,
        CancellationToken token = default) where T : Object
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token, Main.Scene.CurrentToken);
        return await Main.Resource.LoadAssetAsync<T>(key, cacheType, cts.Token);
    }

    public static async UniTask<List<T>> LoadAssetsByLabelAsync<T>(
        string label,
        AssetCacheType cacheType = AssetCacheType.NonRequired,
        CancellationToken token = default) where T : Object
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token, Main.Scene.CurrentToken);
        return await Main.Resource.LoadAssetsByLabelAsync<T>(label, cacheType, cts.Token);
    }

    public static void Release(string key)      => Main.Resource.Release(key);
    public static void ReleaseLabel(string key) => Main.Resource.ReleaseLabel(key);

    #endregion

    #region Pool

    public static async UniTask<T> Instantiate<T>(
        string key = null,
        AssetCacheType cacheType = AssetCacheType.NonRequired,
        CancellationToken token = default) where T : Component
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token, Main.Scene.CurrentToken);
        var ct = cts.Token;

        if (string.IsNullOrEmpty(key)) key = typeof(T).Name;

        T prefab = await Main.Resource.LoadAssetAsync<T>(key, cacheType, ct);
        if (prefab != null)
        {
            prefab.name = key;
            return Object.Instantiate(prefab);
        }

        var go = new GameObject(key, typeof(T));
        return go.GetComponent<T>();
    }

    public static async UniTask<T> SpawnAsync<T>(
        string address,
        Transform parent = null,
        PoolConfig? config = null,
        CancellationToken token = default) where T : Component
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token, Main.Scene.CurrentToken);
        return await Main.Pool.SpawnAsync<T>(address, parent, config, cts.Token);
    }

    public static void Despawn(GameObject go) => Main.Pool.Despawn(go);

    #endregion

    #region UI

    public static async UniTask<T> ShowHud<T>(
        string key = null,
        CancellationToken token = default) where T : UI_Hud
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token, Main.Scene.CurrentToken);
        return await Main.UI.ShowHud<T>(key, cts.Token);
    }

    public static async UniTask<T> ShowOverlay<T>(
        string key = null,
        CancellationToken ct = default) where T : UI_Overlay
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, Main.Scene.CurrentToken);
        return await Main.UI.ShowOverlay<T>(key, cts.Token);
    }

    public static async UniTask<T> ShowPopup<T>(
        string key = null,
        bool clickGuard = true,
        float clickGuardAlpha = -1f,
        bool clickClose = true,
        CancellationToken ct = default) where T : UI_Popup
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, Main.Scene.CurrentToken);
        return await Main.UI.ShowPopup<T>(key, clickGuard, clickGuardAlpha, clickClose, cts.Token);
    }

    public static async UniTask<T> ShowScene<T>(
        string key = null,
        CancellationToken ct = default) where T : UI_Scene
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, Main.Scene.CurrentToken);
        return await Main.UI.ShowScene<T>(key, cts.Token);
    }

    public static void CloseHud()                                  => Main.UI.CloseHud();
    public static void CloseOverlay(UI_Overlay overlay)            => Main.UI.CloseOverlay(overlay);
    public static void CloseAllOverlays()                          => Main.UI.CloseAllOverlays();
    public static void ClosePopup(UI_Popup popup)                  => Main.UI.ClosePopup(popup);
    public static void CloseTopPopup(bool withAnimation = true)    => Main.UI.CloseTopPopup(withAnimation);
    public static void CloseAllPopups(bool withAnimation = false)  => Main.UI.CloseAllPopups();
    public static void CloseScene()                                => Main.UI.CloseScene();

    #endregion
    
    public static async UniTask SceneDelaySeconds(float seconds, CancellationToken token = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token, Main.Scene.CurrentToken);
        await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: cts.Token);
    }
}
