using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public static class AutoMain
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Main.ResetStatics();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeOnStart() => _ = Main.Instance;
}

public class Main : MonoBehaviour
{
    #region Singleton
    private static Main _instance;
    private static bool _initialized;

    private Main() { }
    public static Main Instance
    {
        get
        {
            if (_instance == null) Initialize();
            return _instance;
        }
    }
    #endregion

    #region Static Accessors
    public static ResourceManager Resource => Instance?._resource;
    public static UIManager UI             => Instance?._ui;
    public static LoopManager Loop         => Instance?._loop;
    public static InputManager Input       => Instance?._input;
    public static CameraManager Camera     => Instance?._camera;
    public static PoolManager Pool         => Instance?._pool;
    public static AppStateManager AppState => Instance?._appState;
    public static DataManager Data         => Instance?._data;
    public static AudioManager Audio       => Instance?._audio;
    public static SceneManagerEx Scene     => Instance?._scene;
    public static SafeAreaManager Safe     => Instance?._safe;

#if UNITY_ANDROID || UNITY_IOS || UNITY_EDITOR
    public static HapticManager Haptic     => Instance?._haptic;
#endif
    #endregion

    #region Fields
    private readonly ResourceManager _resource   = new();
    private readonly UIManager _ui               = new();
    private readonly LoopManager _loop           = new();
    private readonly InputManager _input         = new();
    private readonly CameraManager _camera       = new();
    private readonly PoolManager _pool           = new();
    private readonly AppStateManager _appState   = new();
    private readonly DataManager _data           = new();
    private readonly AudioManager _audio         = new();
    private readonly SceneManagerEx _scene       = new();
    private readonly SafeAreaManager _safe       = new();

#if UNITY_ANDROID || UNITY_IOS || UNITY_EDITOR
    private readonly HapticManager _haptic   = new();
#endif
    #endregion

    private static void Initialize()
    {
        if (_instance != null || _initialized) return;
        _initialized = true;
        InitAsync().Forget();
    }

    private static async UniTaskVoid InitAsync()
    {
        CreateMain();
        SetupApplication();
        await InitializeManagers();
    }

    private static void CreateMain()
    {
        GameObject obj = GameObject.Find("@Main");
        if (obj == null) obj = new GameObject("@Main", typeof(Main));

        DontDestroyOnLoad(obj);
        _instance = obj.GetComponent<Main>();
    }

    private static void SetupApplication()
    {
        var setting = Resources.Load<SO_ApplicationSetting>("SO_ApplicationSetting");
        if (setting == null) setting = ScriptableObject.CreateInstance<SO_ApplicationSetting>();

        QualitySettings.vSyncCount = setting.VsyncCount;
        Application.targetFrameRate = setting.TargetFrameRate;
    }

    private static async UniTask InitializeManagers()
    {
        var managers = typeof(Main)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(f => f.GetValue(_instance))
            .OfType<Managers>();

        var groups = managers.GroupBy(m => m.Priority).OrderBy(g => g.Key);

        foreach (var group in groups)
            await UniTask.WhenAll(group.Select(m => m.Initialize()));
    }

    public static void Clear()
    {
        var managers = typeof(Main)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(f => f.GetValue(_instance))
            .OfType<Managers>();

        foreach (var m in managers)
            m.Clear();
    }

    internal static void ResetStatics()
    {
        _instance = null;
        _initialized = false;
    }

    private void Update()
    {
        if (!Loop.IsInitialized) return;
        Loop.Update(Time.unscaledDeltaTime);
        Loop.GameUpdate(Time.unscaledDeltaTime);
    }

    private void LateUpdate()
    {
        if (!Loop.IsInitialized) return;
        Loop.LateUpdate(Time.unscaledDeltaTime);
    }

#if UNITY_ANDROID || UNITY_IOS || UNITY_EDITOR
    private void OnRectTransformDimensionsChange() => _safe?.UpdateSafeArea();
#endif

    private void OnApplicationPause(bool pause) => _appState?.HandleAppStateChange(!pause);
    private void OnApplicationFocus(bool focus)  => _appState?.HandleAppStateChange(focus);

    public new static Coroutine StartCoroutine(IEnumerator coroutine)
        => (Instance as MonoBehaviour).StartCoroutine(coroutine);
    public new static void StopCoroutine(Coroutine coroutine)
        => (Instance as MonoBehaviour).StopCoroutine(coroutine);
}

#region Manager Base Classes
public abstract class Managers
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    public virtual int Priority => 99;
    public bool IsInitialized;

    public async UniTask Initialize()
    {
        if (IsInitialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (IsInitialized) return;
            await OnInitializeAsync();
            IsInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    protected virtual async UniTask OnInitializeAsync() => await UniTask.CompletedTask;
    public virtual void Clear() { }
}

public abstract class PrimaryManager : Managers { public override int Priority => 1; }
public abstract class CoreManager    : Managers { public override int Priority => 2; }
public abstract class ContentManager : Managers { public override int Priority => 3; }
#endregion
