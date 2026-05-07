using UnityEngine;

[CreateAssetMenu(fileName = "SO_ApplicationSetting", menuName = "Main/ApplicationSetting")]
public class SO_ApplicationSetting : ScriptableObject
{
    [Header("Application")]
    [SerializeField] private int _vsyncCount      = 0;
    [SerializeField] private int _targetFrameRate = 60;

    [Header("Default User Settings")]
    [SerializeField] private float  _bgmVolume = 1f;
    [SerializeField] private float  _sfxVolume = 1f;
    [SerializeField] private string _language  = "ko";

    public int    VsyncCount      => _vsyncCount;
    public int    TargetFrameRate => _targetFrameRate;
    public float  BgmVolume       => _bgmVolume;
    public float  SfxVolume       => _sfxVolume;
    public string Language        => _language;
}
