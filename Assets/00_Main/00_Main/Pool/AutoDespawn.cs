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
    private CancellationTokenSource _cts;

    private void Awake()
    {
        _ps = GetComponentInChildren<ParticleSystem>();
    }

    public void OnSpawn()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        ReturnAfterDelay(ResolveDelay(), _cts.Token).Forget();
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

    private async UniTaskVoid ReturnAfterDelay(float delay, CancellationToken ct)
    {
        await UniTask.Delay(TimeSpan.FromSeconds(delay), ignoreTimeScale: ignoreTimeScale, cancellationToken: ct);
        App.Despawn(gameObject);
    }
}
