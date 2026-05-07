using System;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class DataManager : PrimaryManager
{
    #region Properties

    public PlayerData            Player   { get; private set; }
    public SO_ApplicationSetting Defaults { get; private set; }

    public static string PlayerPath =>
        Path.Combine(Application.persistentDataPath, "player.dat");

    private bool _isDirty;

    #endregion

    #region Initialize

    protected override async UniTask OnInitializeAsync()
    {
        Defaults = Resources.Load<SO_ApplicationSetting>("SO_ApplicationSetting")
                   ?? ScriptableObject.CreateInstance<SO_ApplicationSetting>();

        Player = LoadEncrypted<PlayerData>(PlayerPath) ?? CreateFromDefaults();

        MigrateIfNeeded();

        // null 방어: 리스트 필드가 있는 경우 보정
        Player.OwnedItems ??= new System.Collections.Generic.List<string>();

        Main.AppState.OnAppStateBackground += SaveImmediate;

        await UniTask.CompletedTask;
    }

    private PlayerData CreateFromDefaults()
    {
        return new PlayerData
        {
            BgmVolume    = Defaults.BgmVolume,
            SfxVolume    = Defaults.SfxVolume,
            Language     = Defaults.Language,
            TutorialStep = 0,
        };
    }

    #endregion

    #region Migration

    private void MigrateIfNeeded()
    {
        int saved = Player.Version;

        // 버전 마이그레이션 예시:
        // if (saved < 2) MigrateV1ToV2();

        if (Player.Version != saved) SaveImmediate();
    }

    #endregion

    #region Save / Load

    public void MarkDirty()    => _isDirty = true;

    public void ResetPlayer()
    {
        Player = CreateFromDefaults();
        _isDirty = false;
        if (File.Exists(PlayerPath)) File.Delete(PlayerPath);
    }

    public void SaveIfDirty()
    {
        if (!_isDirty) return;
        SaveImmediate();
    }

    public void SaveImmediate()
    {
        Player.LastSavedTick = DateTime.UtcNow.Ticks;
        SaveEncrypted(PlayerPath, Player);
        _isDirty = false;
    }

    private static void SaveEncrypted<T>(string path, T data)
    {
        try
        {
            byte[] plain     = Encoding.UTF8.GetBytes(JsonUtility.ToJson(data));
            byte[] encrypted = DataEncryption.Encrypt(plain);
            File.WriteAllBytes(path, encrypted);
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] Save failed: {e.Message}");
        }
    }

    private static T LoadEncrypted<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            byte[] plain = DataEncryption.Decrypt(File.ReadAllBytes(path));
            return JsonUtility.FromJson<T>(Encoding.UTF8.GetString(plain));
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] Load failed: {e.Message}");
            return null;
        }
    }

    #endregion

    #region Cleanup

    public override void Clear() => SaveIfDirty();

    #endregion
}
