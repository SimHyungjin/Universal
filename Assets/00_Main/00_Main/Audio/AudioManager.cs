using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PrimeTween;
using UnityEngine;
using Object = UnityEngine.Object;

public class AudioManager : CoreManager
{
    private SO_AudioData _data;
    private GameObject   _root;

    // ── BGM (A/B 크로스페이드) ──────────────────────
    private AudioSource _bgmA;
    private AudioSource _bgmB;
    private bool        _usingA = true;
    private BgmType     _currentBgm = BgmType.None;
    private CancellationTokenSource _loopCts;

    // ── SFX 풀 ─────────────────────────────────────
    private readonly Queue<AudioSource>   _sfxPool          = new();
    private readonly HashSet<AudioSource> _activeSfxSources = new();
    private readonly Dictionary<AudioSource, Tween> _volumeTweens = new();
    private const int PoolSize = 8;
    private bool _clearing;

    // ── 채널 볼륨 ───────────────────────────────────
    private float _masterVolume = 1f;
    private float _musicVolume  = 1f;
    private float _soundVolume  = 1f;

    private float MusicVolumeFinal => _masterVolume * _musicVolume;
    private float SoundVolumeFinal => _masterVolume * _soundVolume;

    private AudioSource ActiveBgmSource => _usingA ? _bgmB : _bgmA;

    public BgmType CurrentBgm => _currentBgm;

    #region Initialize

    protected override async UniTask OnInitializeAsync()
    {
        _data = Resources.Load<SO_AudioData>("SO_AudioData");
        if (_data == null)
            Debug.LogWarning("[AudioManager] SO_AudioData not found in Resources.");

        _root = new GameObject("@Audio");
        Object.DontDestroyOnLoad(_root);
        _root.transform.SetSiblingIndex(1);

        _bgmA = CreateSource("BGM_A");
        _bgmB = CreateSource("BGM_B");

        for (int i = 0; i < PoolSize; i++)
            _sfxPool.Enqueue(CreateSource($"SFX_{i}"));

        SyncVolumesFromData();
        await UniTask.CompletedTask;
    }

    private void SyncVolumesFromData()
    {
        var p = Main.Data?.Player;
        if (p == null) return;
        _masterVolume = p.MasterVolume;
        _musicVolume  = p.BgmVolume;
        _soundVolume  = p.SfxVolume;
    }

    #endregion

    #region BGM

    public void PlayBgm(BgmType type, float crossfadeDuration = 1f)
    {
        if (type == _currentBgm) return;
        if (type == BgmType.None) { StopBgm(crossfadeDuration); return; }

        var entry = _data?.GetBgm(type);
        if (entry?.Clip == null) return;

        _currentBgm = type;
        CrossFadeAsync(entry, crossfadeDuration).Forget();
    }

    public void StopBgm(float fadeDuration = 0.5f)
    {
        _currentBgm = BgmType.None;
        CancelLoopMonitor();
        FadeOutAndStop(_bgmA, fadeDuration);
        FadeOutAndStop(_bgmB, fadeDuration);
    }

    private async UniTaskVoid CrossFadeAsync(AudioEntry entry, float duration)
    {
        CancelLoopMonitor();

        var next = _usingA ? _bgmA : _bgmB;
        var prev = _usingA ? _bgmB : _bgmA;
        _usingA = !_usingA;

        float targetVol = entry.Volume * MusicVolumeFinal;

        ApplyToSource(next, entry);
        next.volume = 0f;
        next.Play();

        if (duration > 0f)
        {
            FadeOutAndStop(prev, duration);
            float fadeIn = entry.FadeInDuration > 0f ? entry.FadeInDuration : duration;
            await FadeVolumeAsync(next, targetVol, fadeIn);
        }
        else
        {
            StopVolumeTween(next);
            next.volume = targetVol;
            StopVolumeTween(prev);
            prev.Stop();
        }

        if (entry.Loop && entry.UseLoopPoint)
        {
            _loopCts = new CancellationTokenSource();
            MonitorLoopPointAsync(next, entry, _loopCts.Token).Forget();
        }
    }

    private async UniTaskVoid MonitorLoopPointAsync(AudioSource src, AudioEntry entry, CancellationToken ct)
    {
        float loopEnd = entry.Clip.length - 0.05f;
        while (!ct.IsCancellationRequested && src.isPlaying)
        {
            if (src.time >= loopEnd) src.time = entry.LoopStartTime;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    private void CancelLoopMonitor()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;
    }

    private void FadeOutAndStop(AudioSource src, float duration)
    {
        if (src == null || !src.isPlaying) return;
        if (duration > 0f)
            FadeOutAndStopAsync(src, duration).Forget();
        else
        {
            StopVolumeTween(src);
            src.Stop();
        }
    }

    private async UniTaskVoid FadeOutAndStopAsync(AudioSource src, float duration)
    {
        await FadeVolumeAsync(src, 0f, duration);
        if (src != null) src.Stop();
    }

    #endregion

    #region SFX

    public void PlaySfx(SfxType type, Vector3? position = null)
    {
        if (type == SfxType.None) return;

        var entry = _data?.GetSfx(type);
        if (entry?.Clip == null) return;

        var src = GetPooledSource();
        ApplyToSource(src, entry);

        if (position.HasValue && entry.SpatialBlend > 0f)
            src.transform.position = position.Value;

        float targetVol = entry.Volume * SoundVolumeFinal;

        if (entry.FadeInDuration > 0f)
        {
            src.volume = 0f;
            src.Play();
            FadeVolumeAsync(src, targetVol, entry.FadeInDuration).Forget();
        }
        else
        {
            StopVolumeTween(src);
            src.volume = targetVol;
            src.Play();
        }

        _activeSfxSources.Add(src);
        ReturnToPoolAsync(src, entry).Forget();
    }

    private async UniTaskVoid ReturnToPoolAsync(AudioSource src, AudioEntry entry)
    {
        float duration = entry.Clip.length / Mathf.Abs(entry.Pitch < 0.001f ? 1f : entry.Pitch);

        if (entry.FadeOutDuration > 0f && duration > entry.FadeOutDuration)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(duration - entry.FadeOutDuration));
            if (src.isPlaying)
                await FadeVolumeAsync(src, 0f, entry.FadeOutDuration);
        }
        else
        {
            await UniTask.Delay(TimeSpan.FromSeconds(duration));
        }

        _activeSfxSources.Remove(src);
        if (_clearing) return;

        src.Stop();
        src.clip = null;
        src.transform.SetParent(_root.transform);
        src.transform.localPosition = Vector3.zero;
        _sfxPool.Enqueue(src);
    }

    private async UniTask FadeVolumeAsync(AudioSource src, float targetVolume, float duration)
    {
        if (src == null) return;

        StopVolumeTween(src);

        if (duration <= 0f)
        {
            src.volume = targetVolume;
            return;
        }

        Tween tween = Tween.AudioVolume(src, targetVolume, duration, useUnscaledTime: true);
        _volumeTweens[src] = tween;
        await tween;
    }

    private void StopVolumeTween(AudioSource src)
    {
        if (src == null) return;
        if (!_volumeTweens.TryGetValue(src, out Tween tween)) return;

        if (tween.isAlive) tween.Stop();
        _volumeTweens.Remove(src);
    }

    private AudioSource GetPooledSource()
    {
        if (_sfxPool.Count > 0) return _sfxPool.Dequeue();
        Debug.LogWarning("[AudioManager] SFX pool exhausted, creating extra source.");
        return CreateSource("SFX_extra");
    }

    #endregion

    #region Channel Volume

    public void SetVolume(AudioChannelType channel, float value)
    {
        value = Mathf.Clamp01(value);
        switch (channel)
        {
            case AudioChannelType.Master: _masterVolume = value; break;
            case AudioChannelType.Music:  _musicVolume  = value; break;
            case AudioChannelType.Sound:  _soundVolume  = value; break;
        }

        var p = Main.Data?.Player;
        if (p != null)
        {
            p.MasterVolume = _masterVolume;
            p.BgmVolume    = _musicVolume;
            p.SfxVolume    = _soundVolume;
            Main.Data.MarkDirty();
        }

        var active = ActiveBgmSource;
        if (active.isPlaying)
        {
            var entry = _data?.GetBgm(_currentBgm);
            if (entry != null) active.volume = entry.Volume * MusicVolumeFinal;
        }
    }

    public float GetVolume(AudioChannelType channel) => channel switch
    {
        AudioChannelType.Master => _masterVolume,
        AudioChannelType.Music  => _musicVolume,
        AudioChannelType.Sound  => _soundVolume,
        _                       => 1f,
    };

    #endregion

    #region Utilities

    private void ApplyToSource(AudioSource src, AudioEntry entry)
    {
        src.clip         = entry.Clip;
        src.pitch        = entry.Pitch;
        src.loop         = entry.Loop && !entry.UseLoopPoint;
        src.priority     = entry.Priority;
        src.spatialBlend = entry.SpatialBlend;
        src.minDistance  = entry.MinDistance;
        src.maxDistance  = entry.MaxDistance;
        src.rolloffMode  = entry.RolloffMode;
    }

    private AudioSource CreateSource(string name)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(_root.transform);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        return src;
    }

    #endregion

    #region Cleanup

    public override void Clear()
    {
        _clearing = true;
        StopBgm(0f);

        foreach (var src in _activeSfxSources)
            if (src != null)
            {
                StopVolumeTween(src);
                src.Stop();
            }
        _activeSfxSources.Clear();

        foreach (var src in _sfxPool)
            if (src != null)
            {
                StopVolumeTween(src);
                src.Stop();
            }

        _volumeTweens.Clear();

        _clearing = false;
    }

    #endregion
}
