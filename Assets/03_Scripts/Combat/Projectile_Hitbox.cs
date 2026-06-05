using Cysharp.Threading.Tasks;
using UnityEngine;

// SO_Attack_Data.Projectile 설정으로 스폰되는 직선 발사체. 매 게임 프레임 전방으로 이동하며
// 자기 위치에서 AttackHitEmitter로 잡몹(ECS)·장수(GameObject)를 동시에 판정한다.
// 데미지/넉백/launch/진영은 공격자가 스폰 시 주입한 값을 그대로 쓴다(근접과 동일 SO 출처).
[DisallowMultipleComponent]
public sealed class Projectile_Hitbox : LoopMonoBehaviour, IPoolable
{
    private readonly AttackHitEmitter _emitter = new();
    private readonly AttackHitRegistry _registry = new();

    // 풀 재사용/생성 시 발사체는 풀 루트(0,0,0)에서 켜졌다가 발사 위치로 텔레포트한다.
    // TrailRenderer 점이나 월드-시뮬/스트레치 파티클이 이전 위치에 남으면 화면을 가로지르는 선이 생기므로,
    // 발사/회수 때 트레일·파티클을 모두 비운다.
    private TrailRenderer[] _trails;
    private ParticleSystem[] _particles;

    private SO_Attack_Data _data;
    private float _finalDamage;
    private RangedOwner _owner;
    private Vector3 _direction;
    private float _speed;
    private float _maxDistanceSq;
    private float _lifetime;
    private bool _pierce;
    private bool _spawnFieldOnImpact;
    private AttackHitboxData _hitbox;
    private AttackShapeData _shape;

    private Vector3 _startPos;
    private float _elapsed;
    private bool _hitCuePlayed;
    private bool _active;

    private void Awake()
    {
        _trails = GetComponentsInChildren<TrailRenderer>(true);
        _particles = GetComponentsInChildren<ParticleSystem>(true);
    }

    public void Launch(SO_Attack_Data data, float finalDamage, in RangedOwner owner, Vector3 direction, bool spawnFieldOnImpact = false)
    {
        _data = data;
        _finalDamage = finalDamage;
        _owner = owner;
        _spawnFieldOnImpact = spawnFieldOnImpact;
        _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;

        AttackProjectileData proj = data.Projectile;
        _speed = proj.speed;
        _maxDistanceSq = proj.maxDistance * proj.maxDistance;
        _lifetime = proj.lifetime;
        _pierce = proj.pierce;

        // 히트 볼륨은 발사체 자기 위치 중심. offset/yOffset 0으로 두고 SO의 shape·수직 허용범위를 쓴다.
        _shape = data.Shape;
        _hitbox = new AttackHitboxData
        {
            timing = 0f,
            offset = 0f,
            yOffset = 0f,
            verticalTolerance = data.Hitbox.verticalTolerance
        };

        _startPos = transform.position;
        _elapsed = 0f;
        _hitCuePlayed = false;
        _registry.Clear();
        ClearVfx(); // 텔레포트 직후(위치는 컨트롤러가 이미 설정) 이전 위치 잔상 제거
        _active = true;
    }

    // 텔레포트로 생기는 트레일·파티클 잔상(화면 가로지르는 선) 제거. 파티클은 비운 뒤 모듈 설정대로 다시 방출된다.
    private void ClearVfx()
    {
        if (_trails != null)
            for (int i = 0; i < _trails.Length; i++)
                if (_trails[i] != null) _trails[i].Clear();

        if (_particles != null)
            for (int i = 0; i < _particles.Length; i++)
                if (_particles[i] != null)
                    _particles[i].Clear(false); // 이 시스템의 기존 파티클만 제거(자식은 각자 항목으로 처리됨)
    }

    protected override void OnGameUpdate(float gdt)
    {
        if (!_active) return;

        transform.position += _direction * (_speed * gdt);
        if (_direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(_direction);

        // registry를 유지하므로 관통 시에도 같은 적을 1회만 타격한다.
        bool hit = _emitter.Emit(
            transform.position, _direction, _hitbox, _shape,
            AttackHitInfo.FromMain(_data), _data.HitType, _finalDamage,
            _owner.Faction, _owner.Entity, _registry, scope: 1, hitSameTargetOnce: true, _data);

        if (hit)
            ApplyOnHitEffects();

        if (hit && !_pierce)
        {
            Expire();
            return;
        }

        _elapsed += gdt;
        float traveledSq = (transform.position - _startPos).sqrMagnitude;
        if ((_lifetime > 0f && _elapsed >= _lifetime) ||
            (_maxDistanceSq > 0f && traveledSq >= _maxDistanceSq))
            Expire();
    }

    // 명중 시 공격자 의존 효과(흡혈·게이지)와 전역 효과(히트스톱·히트 카메라컷인)를 근접과 동일하게 적용.
    private void ApplyOnHitEffects()
    {
        CombatOnHit.ApplyAttackerGains(_data, _finalDamage, _owner.Handler, _owner.GaugeGainPerDamage);
        if (!_hitCuePlayed)
        {
            _hitCuePlayed = true;
            CombatOnHit.PlayHitCameraCue(_data);
        }
        CombatOnHit.TriggerHitstop(_data.HitEffects.hitstop, destroyCancellationToken).Forget();
    }

    private void Expire()
    {
        if (!_active) return;
        _active = false;

        // 투척 폭발: 도착/적중 위치에 SO의 장판을 생성. 값은 인자로 캡처해 despawn 후에도 안전하게 스폰한다.
        if (_spawnFieldOnImpact && _data.Field.enabled && !string.IsNullOrEmpty(_data.Field.prefabAddress))
            SpawnImpactFieldAsync(_data, _finalDamage, _owner, transform.position, _direction).Forget();

        App.Despawn(gameObject);
    }

    private static async UniTaskVoid SpawnImpactFieldAsync(
        SO_Attack_Data data, float finalDamage, RangedOwner owner, Vector3 position, Vector3 forward)
    {
        Field_Hitbox field = await App.SpawnAsync<Field_Hitbox>(data.Field.prefabAddress);
        if (field == null) return;
        field.transform.position = position;
        // 임팩트 장판은 고정 위치(추종 없음).
        field.Activate(data, finalDamage, owner, forward, null);
    }

    public void OnSpawn() { }

    public void OnDespawn()
    {
        _active = false;
        _registry.Clear();
        ClearVfx(); // 회수 시에도 비워 다음 재사용 때 잔상이 남지 않게 한다
    }

    private void OnDestroy() => _emitter.Dispose();
}
