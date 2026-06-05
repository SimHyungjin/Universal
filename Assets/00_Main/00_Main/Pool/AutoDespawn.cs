using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[DisallowMultipleComponent]
public class AutoDespawn : MonoBehaviour, IPoolable
{
    [SerializeField] private float duration = 2f;
    [Tooltip("hitstop 등 Time.timeScale 영향을 무시할지 여부")]
    [SerializeField] private bool ignoreTimeScale = false;

    private ParticleSystem _ps;
    private ParticleSystem[] _particles;
    private CancellationTokenSource _cts;

    private void Awake()
    {
        _ps = GetComponentInChildren<ParticleSystem>();
        _particles = GetComponentsInChildren<ParticleSystem>(true);
    }

    public void OnSpawn()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        RestartParticles();
        ReturnAfterDelay(ResolveDelay(), _cts.Token).Forget();
    }

    public void SetDurationAndRestart(float newDuration)
    {
        if (newDuration <= 0f) return;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        RestartParticles();
        ReturnAfterDelay(newDuration, _cts.Token).Forget();
    }

    public void OnDespawn()
    {
        _cts?.Cancel();
    }

    private void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private float ResolveDelay()
    {
        if (duration > 0f) return duration;
        if (_ps != null) return _ps.main.duration + _ps.main.startDelay.constantMax;
        return 2f;
    }

    private void RestartParticles()
    {
        if (_particles == null || _particles.Length == 0)
            _particles = GetComponentsInChildren<ParticleSystem>(true);

        for (int i = 0; i < _particles.Length; i++)
        {
            ParticleSystem particle = _particles[i];
            if (particle == null) continue;
            particle.Clear(true);
            particle.Play(true);
        }
    }

    private async UniTaskVoid ReturnAfterDelay(float delay, CancellationToken ct)
    {
        await UniTask.Delay(TimeSpan.FromSeconds(delay), ignoreTimeScale: ignoreTimeScale, cancellationToken: ct);
        App.Despawn(gameObject);
    }
}
