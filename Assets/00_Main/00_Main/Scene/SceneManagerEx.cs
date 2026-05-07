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
