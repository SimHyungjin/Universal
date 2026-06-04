using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public sealed class Character_Vfx : MonoBehaviour
{
    private const string MotionAfterimagePoolAddress = "MotionAfterimage";

    [Header("Dash VFX")]
    [SerializeField] private string dashStartVfxAddress;
    [SerializeField] private string dashEndVfxAddress;
    [SerializeField] private float dashStartVfxForwardOffset;
    [SerializeField] private float dashEndVfxForwardOffset;
    [SerializeField] private float dashVfxHeight = 0.6f;
    [SerializeField] private bool rotateDashParticlesAgainstDirection = true;

    [Header("Dash Trails")]
    [SerializeField] private TrailRenderer[] dashTrails;
    [SerializeField] private bool clearDashTrailsOnStart = true;

    [Header("Dash Follow Particles")]
    [SerializeField] private ParticleSystem[] dashFollowParticles;
    [SerializeField] private bool clearDashFollowParticlesOnStart = true;
    [SerializeField] private ParticleSystemStopBehavior dashFollowStopBehavior = ParticleSystemStopBehavior.StopEmitting;

    [Header("Motion Afterimages")]
    [SerializeField] private bool enableMotionAfterimages = true;
    [FormerlySerializedAs("dashAfterimageSources")]
    [SerializeField] private SkinnedMeshRenderer[] motionAfterimageSources;
    [FormerlySerializedAs("dashAfterimageMaterial")]
    [SerializeField] private Material motionAfterimageMaterial;
    [FormerlySerializedAs("dashAfterimageColor")]
    [SerializeField] private Color motionAfterimageColor = new(0.45f, 0.95f, 1f, 0.45f);
    [FormerlySerializedAs("dashAfterimagePoolSize")]
    [SerializeField, Min(1)] private int motionAfterimagePoolSize = 24;
    [FormerlySerializedAs("dashAfterimageLifetime")]
    [SerializeField, Min(0.01f)] private float motionAfterimageLifetime = 0.22f;
    [FormerlySerializedAs("dashAfterimageAutoCollectSources")]
    [SerializeField] private bool motionAfterimageAutoCollectSources = true;

    [Header("Dash Afterimages")]
    [SerializeField] private bool enableDashAfterimages = true;
    [SerializeField, Min(0.01f)] private float dashAfterimageInterval = 0.04f;

    [Header("Jump Afterimages")]
    [SerializeField] private bool enableJumpAfterimages = true;
    [SerializeField, Min(1)] private int jumpAfterimageCount = 2;
    [SerializeField, Min(0.01f)] private float jumpAfterimageInterval = 0.04f;

    [Header("Swing Trails")]
    [Tooltip("Weapon roots. Child TrailRenderers are auto-collected. Use one entry for one weapon, multiple entries for dual wield.")]
    [SerializeField] private Transform[] weaponRoots;
    [SerializeField] private bool clearSwingTrailsOnStart = true;

    private static DashAfterimage _motionAfterimagePrefab;
    private static Material _defaultAfterimageMaterial;

    private TrailRenderer[] _swingTrails = Array.Empty<TrailRenderer>();
    private bool _dashAfterimagesPlaying;
    private float _dashAfterimageTimer;
    private int _jumpAfterimagesRemaining;
    private float _jumpAfterimageTimer;
    private Material _resolvedAfterimageMaterial;

    private void Awake()
    {
        EnsureMotionAfterimageSources();
        EnsureMotionAfterimagePoolRegistered(motionAfterimagePoolSize);
        StopDash();
        StopDashFollowParticles(ParticleSystemStopBehavior.StopEmittingAndClear);
        RefreshWeaponTrails();
    }

    private void Update()
    {
        TickDashAfterimages(Time.deltaTime);
        TickJumpAfterimages(Time.deltaTime);
    }

    public void RefreshWeaponTrails()
    {
        System.Collections.Generic.List<TrailRenderer> trails = new();
        if (weaponRoots != null)
        {
            for (int i = 0; i < weaponRoots.Length; i++)
                AppendSwingTrails(weaponRoots[i], trails);
        }

        _swingTrails = trails.Count > 0 ? trails.ToArray() : Array.Empty<TrailRenderer>();
        StopAllSwingTrails();
    }

    public void PlayDashStart(Vector3 direction)
    {
        SetDashTrailsEmitting(true);
        PlayDashFollowParticles();
        StartDashAfterimages();
        SpawnDashVfx(dashStartVfxAddress, direction, dashStartVfxForwardOffset);
    }

    public void PlayDashEnd(Vector3 direction)
    {
        SetDashTrailsEmitting(false);
        StopDashFollowParticles(dashFollowStopBehavior);
        _dashAfterimagesPlaying = false;
        SpawnDashVfx(dashEndVfxAddress, direction, dashEndVfxForwardOffset);
    }

    public void StopDash()
    {
        SetDashTrailsEmitting(false);
        StopDashFollowParticles(dashFollowStopBehavior);
        _dashAfterimagesPlaying = false;
    }

    public void PlayJumpAfterimages()
    {
        StartJumpAfterimages();
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

    private static void AppendSwingTrails(Transform root, System.Collections.Generic.List<TrailRenderer> target)
    {
        if (root == null) return;

        TrailRenderer[] trails = root.GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
        {
            TrailRenderer trail = trails[i];
            if (trail != null && !target.Contains(trail))
                target.Add(trail);
        }
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

    private void EnsureMotionAfterimageSources()
    {
        if (!motionAfterimageAutoCollectSources) return;
        if (motionAfterimageSources != null && motionAfterimageSources.Length > 0) return;

        motionAfterimageSources = GetComponentsInChildren<SkinnedMeshRenderer>(true);
    }

    private static void EnsureMotionAfterimagePoolRegistered(int maxPoolSize)
    {
        if (_motionAfterimagePrefab == null)
        {
            GameObject prefabObject = new(MotionAfterimagePoolAddress, typeof(DashAfterimage));
            prefabObject.SetActive(false);
            prefabObject.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(prefabObject);
            _motionAfterimagePrefab = prefabObject.GetComponent<DashAfterimage>();
        }

        App.RegisterPoolPrefab(
            MotionAfterimagePoolAddress,
            _motionAfterimagePrefab,
            new PoolConfig { InitialSize = 0, MaxSize = Mathf.Max(1, maxPoolSize) });
    }

    private Material ResolveMotionAfterimageMaterial()
    {
        if (motionAfterimageMaterial != null)
            return motionAfterimageMaterial;

        if (_resolvedAfterimageMaterial != null)
            return _resolvedAfterimageMaterial;

        _resolvedAfterimageMaterial = GetDefaultAfterimageMaterial();
        return _resolvedAfterimageMaterial;
    }

    private static Material GetDefaultAfterimageMaterial()
    {
        if (_defaultAfterimageMaterial != null)
            return _defaultAfterimageMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Unlit/Color")
                        ?? Shader.Find("Sprites/Default");
        if (shader == null) return null;

        _defaultAfterimageMaterial = new Material(shader)
        {
            name = "Runtime Dash Afterimage",
            renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent
        };
        ConfigureTransparentMaterial(_defaultAfterimageMaterial);
        return _defaultAfterimageMaterial;
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        if (material == null) return;

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
    }

    private void StartDashAfterimages()
    {
        if (!CanPlayMotionAfterimages() || !enableDashAfterimages) return;

        _dashAfterimagesPlaying = true;
        _dashAfterimageTimer = 0f;
        EmitMotionAfterimage();
    }

    private void TickDashAfterimages(float deltaTime)
    {
        if (!_dashAfterimagesPlaying) return;

        _dashAfterimageTimer -= deltaTime;
        while (_dashAfterimageTimer <= 0f)
        {
            EmitMotionAfterimage();
            _dashAfterimageTimer += dashAfterimageInterval;
        }
    }

    private void StartJumpAfterimages()
    {
        if (!CanPlayMotionAfterimages() || !enableJumpAfterimages) return;

        _jumpAfterimagesRemaining = Mathf.Max(1, jumpAfterimageCount);
        _jumpAfterimageTimer = 0f;
        TickJumpAfterimages(0f);
    }

    private void TickJumpAfterimages(float deltaTime)
    {
        if (_jumpAfterimagesRemaining <= 0) return;

        _jumpAfterimageTimer -= deltaTime;
        while (_jumpAfterimagesRemaining > 0 && _jumpAfterimageTimer <= 0f)
        {
            EmitMotionAfterimage();
            _jumpAfterimagesRemaining--;
            _jumpAfterimageTimer += jumpAfterimageInterval;
        }
    }

    private bool CanPlayMotionAfterimages()
    {
        return enableMotionAfterimages
               && motionAfterimageSources != null
               && motionAfterimageSources.Length > 0
               && ResolveMotionAfterimageMaterial() != null;
    }

    private void EmitMotionAfterimage()
    {
        SpawnMotionAfterimageAsync(destroyCancellationToken).Forget();
    }

    private async UniTaskVoid SpawnMotionAfterimageAsync(CancellationToken token)
    {
        try
        {
            DashAfterimage afterimage = await App.SpawnAsync<DashAfterimage>(
                MotionAfterimagePoolAddress,
                token: token);
            if (afterimage == null) return;

            afterimage.Capture(
                motionAfterimageSources,
                ResolveMotionAfterimageMaterial(),
                motionAfterimageColor,
                motionAfterimageLifetime);
        }
        catch (OperationCanceledException)
        {
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
