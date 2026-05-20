using System;
using Cysharp.Threading.Tasks;
using MapNav.Ecs;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

[DisallowMultipleComponent]
public class Player_Attackcontroller : LoopMonoBehaviour
{
    public int  ComboCount  => _comboCount;
    public bool IsAttacking => _attackTimer > 0f;
    public bool IsInCombo   => _attackTimer > 0f || _comboTimer > 0f;

    private Player_Animator       _playerAnimator;
    private Player_Movecontroller _moveController;
    private SO_AttackData[] _attacks;
    private float _comboWindow = 0.35f;
    private const float ExplicitLookInputSqrThreshold = 0.04f;
    private int   _comboCount;
    private float _attackTimer;
    private float _comboTimer;
    private bool  _nextQueued;
    private Vector3       _pendingLookDirection;
    private SO_AttackData _currentData;
    private bool          _hitboxFired;

    private IHitboxProcessor _hitboxProcessor;

    private EntityManager _em;
    private EntityQuery   _autoAimQuery;
    private World         _cachedWorld;

    private void Awake()
    {
        _playerAnimator = GetComponent<Player_Animator>();
        _moveController = GetComponent<Player_Movecontroller>();
        _hitboxProcessor = GetComponent<IHitboxProcessor>();
    }

    public void SetAttackData(SO_AttackData[] attacks, float comboWindow)
    {
        _attacks = attacks;
        _comboWindow = comboWindow;
    }

    public bool RequestAttack()
    {
        if (_attackTimer > 0f)
        {
            _nextQueued = true;
            return true;
        }

        if (_comboCount > 0 && _comboTimer <= 0f)
            ResetCombo();

        _nextQueued = false;
        StartAttack();
        return IsAttacking;
    }

    protected override void OnGameUpdate(float gdt)
    {
        base.OnGameUpdate(gdt);

        if (_attackTimer > 0f)
        {
            _attackTimer -= gdt;

            if (!_hitboxFired && _attackTimer <= _currentData.Duration * (1f - _currentData.Hitbox.timing))
            {
                _hitboxFired = true;
                FireHitbox(_currentData);
            }

            if (_attackTimer <= 0f) OnAttackEnd();
            return;
        }

        if (_nextQueued)
        {
            _nextQueued = false;
            StartAttack();
            return;
        }

        if (_comboTimer > 0f)
        {
            _comboTimer -= gdt;
            if (_comboTimer <= 0f)
                ResetCombo();
        }
    }

    private void StartAttack()
    {
        SO_AttackData attack = GetData(_comboCount);
        if (attack == null)
        {
            ResetCombo();
            return;
        }

        Vector3 inputAim = _pendingLookDirection;
        bool hasExplicitInput = inputAim.sqrMagnitude > ExplicitLookInputSqrThreshold;
        Vector3 autoAim = hasExplicitInput ? Vector3.zero : FindAutoAimDirection(attack);
        Vector3 lookDir = hasExplicitInput ? inputAim : autoAim;

        if (lookDir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(lookDir);

        App.AlignThirdPersonCameraToTargetYaw(attack.Duration);

        _pendingLookDirection = Vector3.zero;
        _currentData = attack;
        _comboTimer = 0f;
        _attackTimer = _currentData.Duration;
        _hitboxFired = false;

        _playerAnimator?.PlayAttack(_currentData.Animation);
        _moveController?.StartLunge(transform.forward, _currentData.Lunge.distance, _currentData.Lunge.duration);
    }

    private void OnAttackEnd()
    {
        _playerAnimator?.ExitAttack();
        _comboCount++;
        if (_attacks == null || _comboCount >= _attacks.Length)
        {
            ResetCombo();
            return;
        }
        _comboTimer = _comboWindow;
    }

    private void ResetCombo()
    {
        _comboCount = 0;
        _comboTimer = 0f;
        _nextQueued = false;
        _pendingLookDirection = Vector3.zero;
        _hitboxFired = false;
        _playerAnimator?.ReleaseLocomotion();
    }

    public void CancelAttack()
    {
        _attackTimer = 0f;
        _playerAnimator?.ExitAttack();
        ResetCombo();
    }

    private void FireHitbox(SO_AttackData data)
    {
        AttackHitboxData hitbox = data.Hitbox;
        Vector3 center = transform.position
            + transform.forward * hitbox.offset
            + Vector3.up * hitbox.height;

        bool didHit = false;

        Collider[] cols = Physics.OverlapSphere(center, hitbox.radius);
        foreach (Collider col in cols)
        {
            if (!col.TryGetComponent(out IHitTarget target)) continue;
            target.ReceiveHit(transform.position, transform.forward, data);
            SpawnHitFeedback(data, col.transform.position);
            didHit = true;
        }

        if (_hitboxProcessor != null && _hitboxProcessor.Process(data, transform))
            didHit = true;

        if (didHit) TriggerHitstop(data.Hitstop).Forget();
    }

    public void UpdateLookDirection(Vector3 worldInput)
    {
        _pendingLookDirection = worldInput;
    }

    private Vector3 FindAutoAimDirection(SO_AttackData data)
    {
        float range = data.Hitbox.offset + data.Hitbox.radius;
        Vector3 best = Vector3.zero;
        float bestDist = range * range;
        Vector3 myPos = transform.position;
        Vector3 forward = transform.forward;
        forward.y = 0f;

        // GameObject 타겟
        Collider[] cols = Physics.OverlapSphere(myPos, bestDist);
        foreach (Collider col in cols)
        {
            if (!col.TryGetComponent(out IHitTarget _)) continue;
            Vector3 diff = col.transform.position - myPos;
            diff.y = 0f;
            if (diff.sqrMagnitude < 0.0001f) continue;
            if (Vector3.Dot(diff.normalized, forward) < -0.5f) continue;
            float dist = diff.sqrMagnitude;
            if (dist >= bestDist) continue;
            bestDist = dist;
            best = diff.normalized;
        }

        // ECS 타겟
        if (EnsureAutoAimQuery())
        {
            NativeArray<LocalTransform> transforms = _autoAimQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < transforms.Length; i++)
            {
                var f = transforms[i].Position;
                Vector3 pos = new Vector3(f.x, f.y, f.z);
                Vector3 diff = pos - myPos;
                diff.y = 0f;
                if (diff.sqrMagnitude < 0.0001f) continue;
                if (Vector3.Dot(diff.normalized, forward) < -0.5f) continue;
                float dist = diff.sqrMagnitude;
                if (dist >= bestDist) continue;
                bestDist = dist;
                best = diff.normalized;
            }
            transforms.Dispose();
        }

        return best;
    }

    private bool EnsureAutoAimQuery()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;
        if (world == _cachedWorld) return true;

        if (_cachedWorld != null && _cachedWorld.IsCreated)
            _autoAimQuery.Dispose();

        _cachedWorld = world;
        _em = world.EntityManager;
        _autoAimQuery = _em.CreateEntityQuery(
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<NavAgentKnockback>());
        return true;
    }

    private SO_AttackData GetData(int index)
        => _attacks != null && _attacks.Length > 0
            ? _attacks[Mathf.Clamp(index, 0, _attacks.Length - 1)]
            : null;

    private void SpawnHitFeedback(SO_AttackData data, Vector3 position)
        => CombatFeedback.PlayHitFeedback(data, position, destroyCancellationToken);

    private async UniTaskVoid TriggerHitstop(AttackHitstopData hitstop)
    {
        if (hitstop.duration <= 0f) return;

        Main.Loop.SetGameSpeed(hitstop.timeScale);
        await UniTask.Delay(
            TimeSpan.FromSeconds(hitstop.duration),
            ignoreTimeScale: true,
            cancellationToken: destroyCancellationToken);
        Main.Loop.SetGameSpeed(1f);
    }

    private void OnDestroy()
    {
        if (_cachedWorld != null && _cachedWorld.IsCreated)
            _autoAimQuery.Dispose();
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || _currentData == null || !IsAttacking) return;

        AttackHitboxData hitbox = _currentData.Hitbox;
        Vector3 center = transform.position
            + transform.forward * hitbox.offset
            + Vector3.up * hitbox.height;

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.35f);
        Gizmos.DrawSphere(center, hitbox.radius);
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(center, hitbox.radius);
    }
}
