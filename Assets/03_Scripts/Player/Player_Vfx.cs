using UnityEngine;

[DisallowMultipleComponent]
public sealed class Player_Vfx : MonoBehaviour
{
    [Header("Dash")]
    [SerializeField] private TrailRenderer[] dashTrails;
    [SerializeField] private ParticleSystem dashStartFx;
    [SerializeField] private ParticleSystem dashEndFx;
    [SerializeField] private bool clearDashTrailsOnStart = true;
    [SerializeField] private bool rotateDashParticlesAgainstDirection = true;

    private void Awake()
    {
        StopDash();
        StopParticle(dashStartFx);
        StopParticle(dashEndFx);
    }

    public void PlayDashStart(Vector3 direction)
    {
        SetDashTrailsEmitting(true);
        PlayDashParticle(dashStartFx, direction);
    }

    public void PlayDashEnd(Vector3 direction)
    {
        SetDashTrailsEmitting(false);
        PlayDashParticle(dashEndFx, direction);
    }

    public void StopDash()
    {
        SetDashTrailsEmitting(false);
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

    private void PlayDashParticle(ParticleSystem particle, Vector3 direction)
    {
        if (particle == null) return;

        if (rotateDashParticlesAgainstDirection)
        {
            Vector3 planarDirection = new Vector3(direction.x, 0f, direction.z);
            if (planarDirection.sqrMagnitude > 0.0001f)
                particle.transform.rotation = Quaternion.LookRotation(-planarDirection.normalized, Vector3.up);
        }

        particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particle.Play(true);
    }

    private static void StopParticle(ParticleSystem particle)
    {
        if (particle == null) return;

        particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
