using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class Player_Vfx : MonoBehaviour
{
    [Header("Dash")]
    [SerializeField] private TrailRenderer[] dashTrails;
    [SerializeField] private string dashStartVfxAddress;
    [SerializeField] private string dashEndVfxAddress;
    [SerializeField] private float dashStartVfxForwardOffset;
    [SerializeField] private float dashEndVfxForwardOffset;
    [SerializeField] private float dashVfxHeight = 0.6f;
    [SerializeField] private bool clearDashTrailsOnStart = true;
    [SerializeField] private bool rotateDashParticlesAgainstDirection = true;
    [SerializeField] private ParticleSystem[] dashFollowParticles;
    [SerializeField] private bool clearDashFollowParticlesOnStart = true;
    [SerializeField] private ParticleSystemStopBehavior dashFollowStopBehavior = ParticleSystemStopBehavior.StopEmitting;

    [Header("Swing Trails")]
    [Tooltip("무기 위치. 자식의 모든 TrailRenderer를 자동 수집한다. 무기 교체 시 RefreshWeaponTrails() 호출.")]
    [SerializeField] private Transform weaponRoot;
    [SerializeField] private bool clearSwingTrailsOnStart = true;

    private TrailRenderer[] _swingTrails = Array.Empty<TrailRenderer>();

    private void Awake()
    {
        StopDash();
        StopDashFollowParticles(ParticleSystemStopBehavior.StopEmittingAndClear);
        RefreshWeaponTrails();
    }

    public void RefreshWeaponTrails()
    {
        _swingTrails = weaponRoot != null
            ? weaponRoot.GetComponentsInChildren<TrailRenderer>(true)
            : Array.Empty<TrailRenderer>();
        StopAllSwingTrails();
    }

    public void PlayDashStart(Vector3 direction)
    {
        SetDashTrailsEmitting(true);
        PlayDashFollowParticles();
        SpawnDashVfx(dashStartVfxAddress, direction, dashStartVfxForwardOffset);
    }

    public void PlayDashEnd(Vector3 direction)
    {
        SetDashTrailsEmitting(false);
        StopDashFollowParticles(dashFollowStopBehavior);
        SpawnDashVfx(dashEndVfxAddress, direction, dashEndVfxForwardOffset);
    }

    public void StopDash()
    {
        SetDashTrailsEmitting(false);
        StopDashFollowParticles(dashFollowStopBehavior);
    }

    public void PlaySwingTrails(string[] ids)
    {
        if (ids == null || ids.Length == 0) return;

        for (int i = 0; i < _swingTrails.Length; i++)
        {
            TrailRenderer trail = _swingTrails[i];
            if (trail == null || !ContainsId(ids, trail.name)) continue;

            if (clearSwingTrailsOnStart)
                trail.Clear();
            trail.emitting = true;
        }
    }

    public void StopSwingTrails(string[] ids)
    {
        if (ids == null || ids.Length == 0) return;

        for (int i = 0; i < _swingTrails.Length; i++)
        {
            TrailRenderer trail = _swingTrails[i];
            if (trail == null || !ContainsId(ids, trail.name)) continue;
            trail.emitting = false;
        }
    }

    public void StopAllSwingTrails()
    {
        for (int i = 0; i < _swingTrails.Length; i++)
        {
            TrailRenderer trail = _swingTrails[i];
            if (trail == null) continue;
            trail.emitting = false;
        }
    }

    private static bool ContainsId(string[] ids, string name)
    {
        for (int i = 0; i < ids.Length; i++)
        {
            if (ids[i] == name) return true;
        }
        return false;
    }

    private void SetDashTrailsEmitting(bool emitting)
    {
        if (dashTrails == null) return;

        for (int i = 0; i < dashTrails.Length; i++)
        {
            TrailRenderer trail = dashTrails[i];
            if (trail == null) continue;

            if (emitting && clearDashTrailsOnStart)
                trail.Clear();

            trail.emitting = emitting;
        }
    }

    private void PlayDashFollowParticles()
    {
        if (dashFollowParticles == null) return;

        for (int i = 0; i < dashFollowParticles.Length; i++)
        {
            ParticleSystem particle = dashFollowParticles[i];
            if (particle == null) continue;

            if (clearDashFollowParticlesOnStart)
                particle.Clear(true);

            particle.Play(true);
        }
    }

    private void StopDashFollowParticles(ParticleSystemStopBehavior stopBehavior)
    {
        if (dashFollowParticles == null) return;

        for (int i = 0; i < dashFollowParticles.Length; i++)
        {
            ParticleSystem particle = dashFollowParticles[i];
            if (particle == null) continue;

            particle.Stop(true, stopBehavior);
        }
    }

    private void SpawnDashVfx(string address, Vector3 direction, float forwardOffset)
    {
        if (string.IsNullOrWhiteSpace(address)) return;

        SpawnDashVfxAsync(address, direction, forwardOffset, destroyCancellationToken).Forget();
    }

    private async UniTaskVoid SpawnDashVfxAsync(string address, Vector3 direction, float forwardOffset, CancellationToken token)
    {
        try
        {
            AutoDespawn vfx = await App.SpawnAsync<AutoDespawn>(address, token: token);
            if (vfx == null) return;

            ResolveDashVfxPose(direction, forwardOffset, out Vector3 position, out Quaternion rotation);
            vfx.transform.position = position;

            if (rotateDashParticlesAgainstDirection)
                vfx.transform.rotation = rotation;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ResolveDashVfxPose(Vector3 direction, float forwardOffset, out Vector3 position, out Quaternion rotation)
    {
        Vector3 planarDirection = new(direction.x, 0f, direction.z);
        if (planarDirection.sqrMagnitude <= 0.0001f)
            planarDirection = new Vector3(transform.forward.x, 0f, transform.forward.z);
        if (planarDirection.sqrMagnitude <= 0.0001f)
            planarDirection = Vector3.forward;

        planarDirection.Normalize();
        position = transform.position + Vector3.up * dashVfxHeight + planarDirection * forwardOffset;

        rotation = Quaternion.LookRotation(-planarDirection, Vector3.up);
    }
}
