using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

public abstract class SceneBase
{
    public abstract UniTask EnterScene(CancellationToken token);
    public abstract void ExitScene();
}

public class SceneManagerEx : ContentManager
{
    private SceneBase _currentScene;
    private CancellationTokenSource _cts = new();
    private bool _isTransitioning;

    public CancellationToken CurrentToken => _cts.Token;

    protected override async UniTask OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        string sceneName = SceneManager.GetActiveScene().name;
        await CreateAndEnterScene(sceneName, _cts.Token);
    }

    public async UniTask ChangeSceneAsync(
        string sceneName,
        Func<UniTask> onBeforeLoad = null,
        Func<UniTask> onAfterLoad  = null)
    {
        if (_isTransitioning || string.IsNullOrEmpty(sceneName)) return;
        _isTransitioning = true;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            if (onBeforeLoad != null) await onBeforeLoad().AttachExternalCancellation(token);

            _currentScene?.ExitScene();
            Main.Data?.SaveIfDirty();
            Main.Clear();

            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            op.allowSceneActivation = false;

            while (op.progress < 0.9f)
                await UniTask.Yield(token);

            op.allowSceneActivation = true;
            await op.ToUniTask(cancellationToken: token);

            Scene loadedScene = SceneManager.GetSceneByName(sceneName);
            if (loadedScene.IsValid()) SceneManager.SetActiveScene(loadedScene);

            await UnloadOldScenes(sceneName, token);

            if (onAfterLoad != null) await onAfterLoad().AttachExternalCancellation(token);
            await CreateAndEnterScene(sceneName, token);
        }
        catch (OperationCanceledException)
        {
            Debug.Log($"Scene transition to {sceneName} was canceled.");
        }
        finally
        {
            _isTransitioning = false;
        }
    }

    /// <summary>
    /// 현재 씬을 깨끗이 재로드합니다(한 판 재시작 등). ChangeSceneAsync는 동일 이름 씬을 언로드 대상에서
    /// 제외하므로 같은 씬 재시작에는 쓸 수 없다 — 여기서는 직전 씬을 핸들로 직접 언로드한다.
    /// </summary>
    public async UniTask ReloadCurrentSceneAsync()
    {
        if (_isTransitioning) return;
        _isTransitioning = true;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            Scene oldScene = SceneManager.GetActiveScene();
            string sceneName = oldScene.name;

            _currentScene?.ExitScene();
            Main.Data?.SaveIfDirty();
            Main.Clear();

            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            op.allowSceneActivation = false;

            while (op.progress < 0.9f)
                await UniTask.Yield(token);

            op.allowSceneActivation = true;
            await op.ToUniTask(cancellationToken: token);

            // 새로 로드된 씬은 같은 이름이지만 oldScene과 다른 핸들이다 — 그걸 활성화하고 옛 씬을 언로드한다.
            Scene newScene = FindLoadedSceneByName(sceneName, oldScene);
            if (newScene.IsValid()) SceneManager.SetActiveScene(newScene);

            if (oldScene.IsValid() && oldScene.isLoaded)
                await SceneManager.UnloadSceneAsync(oldScene).ToUniTask(cancellationToken: token);

            await CreateAndEnterScene(sceneName, token);
        }
        catch (OperationCanceledException)
        {
            Debug.Log("Scene reload was canceled.");
        }
        finally
        {
            _isTransitioning = false;
        }
    }

    // 같은 이름의 로드된 씬 중 exclude가 아닌 첫 번째(= 방금 additive로 새로 로드된 씬)를 찾는다.
    private static Scene FindLoadedSceneByName(string sceneName, Scene exclude)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (s.name == sceneName && s != exclude) return s;
        }
        return default;
    }

    private async UniTask UnloadOldScenes(string currentSceneName, CancellationToken token)
    {
        var scenesToUnload = new List<Scene>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.name == currentSceneName || scene.name == "InitScene") continue;
            scenesToUnload.Add(scene);
        }

        foreach (var scene in scenesToUnload)
            if (scene.isLoaded)
                await SceneManager.UnloadSceneAsync(scene).ToUniTask(cancellationToken: token);
    }

    private async UniTask CreateAndEnterScene(string sceneName, CancellationToken token)
    {
        Type sceneType = Type.GetType(sceneName);
        if (sceneType != null && typeof(SceneBase).IsAssignableFrom(sceneType))
        {
            _currentScene = Activator.CreateInstance(sceneType) as SceneBase;
            if (_currentScene != null) await _currentScene.EnterScene(token);
        }
    }
}
