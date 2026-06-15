using Cysharp.Threading.Tasks;
using UnityEngine;

// AttackProjectileDelivery(delivery 이벤트) 설정으로 스폰되는 직선 발사체. 매 게임 프레임 전방으로 이동하며
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
    private AttackHitInfo _hitInfo;
    private HitType _hitType;
    private AttackHitResultData _hitResult;
    private AttackFieldDelivery _impactField;
    private AttackHitResultData _impactFieldHitResult;
    private float _impactFieldDuration;
    private float _impactFieldFinalDamage;
    private bool _useDeliveryImpactField;

    private Vector3 _startPos;
    private float _elapsed;
    private bool _hitCuePlayed;
    private bool _active;

    private void Awake()
    {
        _trails = GetComponentsInChildren<TrailRenderer>(true);
        _particles = GetComponentsInChildren<ParticleSystem>(true);
    }

    public void Launch(
        SO_Attack_Data data,
        in AttackProjectileDelivery delivery,
        in AttackHitResultData hitResult,
        float finalDamage,
        in RangedOwner owner,
        Vector3 direction,
        bool spawnFieldOnImpact = false,
        AttackFieldDelivery impactField = default,
        AttackHitResultData impactFieldHitResult = default,
        float impactFieldDuration = 0f,
        float impactFieldFinalDamage = 0f)
    {
        _data = data;
        _finalDamage = finalDamage;
        _owner = owner;
        _spawnFieldOnImpact = spawnFieldOnImpact;
        _impactField = impactField;
        _impactFieldHitResult = impactFieldHitResult;
        _impactFieldDuration = impactFieldDuration;
        _impactFieldFinalDamage = impactFieldFinalDamage;
        _useDeliveryImpactField = spawnFieldOnImpact && !string.IsNullOrEmpty(impactField.prefabAddress);
        _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;

        _speed = delivery.speed;
        _maxDistanceSq = delivery.maxDistance * delivery.maxDistance;
        _lifetime = delivery.lifetime;
        _pierce = delivery.pierce;
        _shape = delivery.shape;
        _hitbox = delivery.hitbox;
        _hitInfo = AttackHitInfo.FromHitResult(hitResult);
        _hitType = hitResult.hitType;
        _hitResult = hitResult;

        _startPos = transform.position;
        _elapsed = 0f;
        _hitCuePlayed = false;
        _registry.Clear();
        ClearVfx();
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
            _hitInfo, _hitType, _finalDamage,
            _owner.Faction, _owner.Entity, _registry, scope: 1, hitSameTargetOnce: true, _data,
            useFeedbackOverride: true,
            hitSfxOverride: _hitResult.hitSfx,
            hitVfxOverride: _hitResult.hitVfxAddress);

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

    // 명중 시 공격자 의존 효과(흡혈·게이지)는 누구나 적용. 플레이어 시점 juice(컷인·카메라 셰이크·전역 히트스톱)는
    // 로컬 플레이어가 쏜 것일 때만(적/아군 AI 발사체는 SFX/VFX만 — 화면 흔들기·시간 정지 없음).
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

        // 투척 폭발: 도착/적중 위치에 SO의 장판을 생성. 값은 인자로 캡처해 despawn 후에도 안전하게 스폰한다.
        if (_spawnFieldOnImpact && _useDeliveryImpactField)
            SpawnImpactFieldAsync(_data, _impactField, _impactFieldHitResult, _impactFieldDuration, _impactFieldFinalDamage, _owner, transform.position, _direction).Forget();

        App.Despawn(gameObject);
    }

    private static async UniTaskVoid SpawnImpactFieldAsync(
        SO_Attack_Data data, AttackFieldDelivery delivery, AttackHitResultData hitResult, float duration, float finalDamage, RangedOwner owner, Vector3 position, Vector3 forward)
    {
        Field_Hitbox field = await App.SpawnAsync<Field_Hitbox>(delivery.prefabAddress);
        if (field == null) return;
        field.transform.position = position;
        field.Activate(data, delivery, hitResult, duration, finalDamage, owner, forward, null);
    }

    public void OnSpawn() { }

    public void OnDespawn()
    {
        _active = false;
        _useDeliveryImpactField = false;
        _registry.Clear();
        ClearVfx(); // 회수 시에도 비워 다음 재사용 때 잔상이 남지 않게 한다
    }

    private void OnDestroy() => _emitter.Dispose();
}
