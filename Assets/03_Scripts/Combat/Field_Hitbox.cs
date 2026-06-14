using Cysharp.Threading.Tasks;
using UnityEngine;

// SO_Attack_Data.Field 설정으로 스폰되는 지속 장판. duration 동안 유지되며 tickInterval마다
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

    private float _elapsed;
    private float _nextTick;
    private bool _hitCuePlayed;
    private bool _active;

    private void Awake()
    {
        _trails = GetComponentsInChildren<TrailRenderer>(true);
        _particles = GetComponentsInChildren<ParticleSystem>(true);
    }

    public void Activate(SO_Attack_Data data, float finalDamage, in RangedOwner owner, Vector3 forward, Transform followTarget)
    {
        _data = data;
        _finalDamage = finalDamage;
        _owner = owner;
        _followTarget = followTarget;
        _forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;

        AttackFieldData field = data.Field;
        _forwardOffset = field.forwardOffset;
        _duration = field.duration;
        _tickInterval = Mathf.Max(0.01f, field.tickInterval);

        // 히트 볼륨은 장판 자기 위치 중심. offset/yOffset 0으로 두고 SO의 shape·수직 허용범위를 쓴다.
        _shape = data.Shape;
        _hitbox = new AttackHitboxData
        {
            timing = 0f,
            offset = 0f,
            yOffset = 0f,
            verticalTolerance = data.Hitbox.verticalTolerance
        };

        _elapsed = 0f;
        _nextTick = 0f; // 활성화 즉시 1틱
        _hitCuePlayed = false;
        ClearVfx(); // 스폰 위치로 옮긴 직후 이전 위치 잔상 제거
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
                AttackHitInfo.FromMain(_data), _data.HitType, _finalDamage,
                _owner.Faction, _owner.Entity, _registry, scope: 1, hitSameTargetOnce: true, _data);

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
        CombatOnHit.ApplyAttackerGains(_data, _finalDamage, _owner.Handler, _owner.GaugeGainPerDamage);

        if (!PlayerController.IsLocalPlayer(_owner.Handler))
            return;

        if (!_hitCuePlayed)
        {
            _hitCuePlayed = true;
            CombatOnHit.PlayHitCameraCue(_data);
        }
        CombatFeedback.PlayHitCameraShake(_data);
        CombatOnHit.TriggerHitstop(_data.HitEffects.hitstop, destroyCancellationToken).Forget();
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
