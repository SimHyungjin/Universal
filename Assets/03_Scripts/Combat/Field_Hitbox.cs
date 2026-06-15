using Cysharp.Threading.Tasks;
using UnityEngine;

// AttackFieldDelivery(delivery 이벤트) 설정으로 스폰되는 지속 장판. field.lifetime 동안 유지되며 tickInterval마다
// 자기 위치에서 AttackHitEmitter로 잡몹(ECS)·장수(GameObject)를 판정한다. 틱마다 레지스트리를
// 비워 같은 적을 매 틱 재타격한다(근접 repeat와 동일 개념).
[DisallowMultipleComponent]
public sealed class Field_Hitbox : LoopMonoBehaviour, IPoolable
{
    private readonly AttackHitEmitter _emitter = new();
    private readonly AttackHitRegistry _registry = new();

    // 풀 루트에서 켜진 뒤 스폰 위치로 옮겨질 때 생기는 트레일·파티클 잔상 제거용.
    private TrailRenderer[] _trails;
    private ParticleSystem[] _particles;

    private SO_Attack_Data _data;
    private float _finalDamage;
    private RangedOwner _owner;
    private Transform _followTarget;
    private Vector3 _forward;
    private float _forwardOffset;
    private float _duration;
    private float _tickInterval;
    private AttackHitboxData _hitbox;
    private AttackShapeData _shape;
    private AttackHitInfo _hitInfo;
    private HitType _hitType;
    private AttackHitResultData _hitResult;

    private float _elapsed;
    private float _nextTick;
    private bool _hitCuePlayed;
    private bool _active;

    private void Awake()
    {
        _trails = GetComponentsInChildren<TrailRenderer>(true);
        _particles = GetComponentsInChildren<ParticleSystem>(true);
    }

    public void Activate(
        SO_Attack_Data data,
        in AttackFieldDelivery delivery,
        in AttackHitResultData hitResult,
        float duration,
        float finalDamage,
        in RangedOwner owner,
        Vector3 forward,
        Transform followTarget)
    {
        _data = data;
        _finalDamage = finalDamage;
        _owner = owner;
        _followTarget = followTarget;
        _forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;

        _forwardOffset = delivery.forwardOffset;
        _duration = duration;
        _tickInterval = Mathf.Max(0.01f, delivery.tickInterval);
        _shape = delivery.shape;
        _hitbox = delivery.hitbox;
        _hitInfo = AttackHitInfo.FromHitResult(hitResult);
        _hitType = hitResult.hitType;
        _hitResult = hitResult;

        _elapsed = 0f;
        _nextTick = 0f;
        _hitCuePlayed = false;
        ClearVfx();
        _active = true;
    }

    private void ClearVfx()
    {
        if (_trails != null)
            for (int i = 0; i < _trails.Length; i++)
                if (_trails[i] != null) _trails[i].Clear();

        if (_particles != null)
            for (int i = 0; i < _particles.Length; i++)
                if (_particles[i] != null) _particles[i].Clear(false);
    }

    protected override void OnGameUpdate(float gdt)
    {
        if (!_active) return;

        // followAttacker: 공격자를 추종. 공격자가 디스폰되면(Unity null) 마지막 위치를 유지한다.
        if (_followTarget != null)
            transform.position = _followTarget.position + _forward * _forwardOffset;

        _elapsed += gdt;
        if (_elapsed >= _nextTick)
        {
            _nextTick += _tickInterval;
            _registry.Clear(); // 틱마다 재타격
            bool hit = _emitter.Emit(
                transform.position, _forward, _hitbox, _shape,
                _hitInfo, _hitType, _finalDamage,
                _owner.Faction, _owner.Entity, _registry, scope: 1, hitSameTargetOnce: true, _data,
                useFeedbackOverride: true,
                hitSfxOverride: _hitResult.hitSfx,
                hitVfxOverride: _hitResult.hitVfxAddress);

            if (hit)
                ApplyOnHitEffects();
        }

        if (_duration > 0f && _elapsed >= _duration)
            Expire();
    }

    // 명중 시 공격자 의존 효과(흡혈·게이지)는 누구나 적용. 플레이어 시점 juice(컷인·카메라 셰이크·전역 히트스톱)는
    // 로컬 플레이어가 깐 것일 때만(적/아군 AI 장판은 SFX/VFX만 — 화면 흔들기·시간 정지 없음).
    private void ApplyOnHitEffects()
    {
        CombatOnHit.ApplyAttackerGains(_hitResult.lifeSteal, _finalDamage, _owner.Handler, _owner.GaugeGainPerDamage);

        if (!PlayerController.IsLocalPlayer(_owner.Handler))
            return;

        if (!_hitCuePlayed)
        {
            _hitCuePlayed = true;
            if (_hitResult.cameraCue.enabled)
                CombatOnHit.PlayCameraCue(_hitResult.cameraCue, _data.TotalDuration);
        }
        if (_hitResult.cameraShake.enabled)
            App.ShakeCamera(_hitResult.cameraShake.amplitude, _hitResult.cameraShake.duration, _hitResult.cameraShake.frequency);
        CombatOnHit.TriggerHitstop(_hitResult.hitstop, destroyCancellationToken).Forget();
    }

    private void Expire()
    {
        if (!_active) return;
        _active = false;
        App.Despawn(gameObject);
    }

    public void OnSpawn() { }

    public void OnDespawn()
    {
        _active = false;
        _followTarget = null;
        _registry.Clear();
        ClearVfx();
    }

    private void OnDestroy() => _emitter.Dispose();
}
