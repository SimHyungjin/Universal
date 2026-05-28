using MapNav.Ecs;
using Unity.Entities;
using UnityEngine;

// ECS의 PlayerIncomingHit 버퍼를 드레인해 Player_ActionHandler.ReceiveHit으로 라우팅한다.
// 잡몹 공격 데이터는 잡몹 entity의 NavAgentAttackProfile에 베이크되어 버퍼에 그대로 실려온다.
// 이 컴포넌트는 SO 참조를 들지 않으며, raw 값만으로 ReceiveHit + CombatFeedback을 호출한다.
[DisallowMultipleComponent]
[RequireComponent(typeof(Player_ActionHandler))]
public sealed class Player_HitReceiver : MonoBehaviour
{
    // hit VFX는 캐릭터의 가슴/배 높이에서 떠야 자연스럽다. 발 위치 기준으로 +0.5m 보정.
    private const float HitVfxHeightOffset = 0.5f;

    private Player_ActionHandler _actionHandler;
    private World _world;
    private EntityQuery _singletonQuery;

    private void Awake()
    {
        _actionHandler = GetComponent<Player_ActionHandler>();
    }

    private void LateUpdate()
    {
        if (_actionHandler == null) return;
        if (!EnsureQuery(out EntityManager em)) return;
        if (_singletonQuery.IsEmpty) return;

        Entity singleton = _singletonQuery.GetSingletonEntity();
        if (!em.HasBuffer<PlayerIncomingHit>(singleton)) return;

        DynamicBuffer<PlayerIncomingHit> inbox = em.GetBuffer<PlayerIncomingHit>(singleton);
        int count = inbox.Length;
        if (count == 0) return;

        for (int i = 0; i < count; i++)
        {
            PlayerIncomingHit hit = inbox[i];
            ApplyHit((Vector3)hit.SourcePosition, hit.Attack);
        }

        inbox.Clear();
    }

    private void ApplyHit(Vector3 source, NavAgentAttackProfile profile)
    {
        if (_actionHandler.IsInvincible || _actionHandler.State == PlayerActionState.Dead)
            return;

        AttackKnockbackData knockback = new AttackKnockbackData
        {
            type     = profile.KnockbackType,
            force    = profile.KnockbackForce,
            duration = profile.KnockbackDuration,
            friction = profile.KnockbackFriction
        };
        AttackHitstopData hitstop = new AttackHitstopData
        {
            duration  = profile.HitstopDuration,
            timeScale = profile.HitstopTimeScale
        };
        AttackDownData down = new AttackDownData
        {
            enabled  = profile.IsDownAttack != 0,
            duration = profile.DownDuration
        };

        _actionHandler.ReceiveHit(
            source,
            knockback,
            profile.Damage,
            hitstop,
            down,
            profile.SuperArmorBreak);

        // VFX는 피격자(플레이어) 쪽에서 — 잡몹 위치(source)는 넉백 방향 계산용일 뿐
        Vector3 vfxPosition = transform.position;
        Vector3 toAttacker = source - vfxPosition;
        toAttacker.y = 0f;
        if (toAttacker.sqrMagnitude > 0.0001f)
            vfxPosition += toAttacker.normalized * 0.3f;
        vfxPosition.y += HitVfxHeightOffset;
        CombatFeedback.PlayHitFeedback(profile.HitSfx, profile.HitVfxAddress.ToString(), vfxPosition, destroyCancellationToken);
    }

    private bool EnsureQuery(out EntityManager em)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            _world = null;
            em = default;
            return false;
        }

        em = world.EntityManager;
        if (_world == world) return true;

        _world = world;
        _singletonQuery = em.CreateEntityQuery(typeof(PlayerNavTarget));
        return true;
    }
}
