using MapNav.Ecs;
using Unity.Entities;
using UnityEngine;

// 자기 캐릭터의 CharacterIncomingHit 버퍼를 드레인해 Character_ActionHandler.ReceiveHit으로 라우팅한다.
// 어느 엔티티를 드레인할지는 같은 GameObject의 Character_EcsBridge가 소유한 엔티티를 따른다(다중 캐릭터 지원).
// 잡몹 공격 데이터는 잡몹 entity의 NavAgentAttackProfile에 베이크되어 버퍼에 그대로 실려온다.
// 이 컴포넌트는 SO 참조를 들지 않으며, raw 값만으로 ReceiveHit + CombatFeedback을 호출한다.
[DisallowMultipleComponent]
[RequireComponent(typeof(Character_ActionHandler))]
[RequireComponent(typeof(Character_EcsBridge))]
public sealed class Character_EcsHitReceiver : MonoBehaviour
{
    // hit VFX는 캐릭터의 가슴/배 높이에서 떠야 자연스럽다. 발 위치 기준으로 +0.5m 보정.
    private const float HitVfxHeightOffset = 0.5f;

    private Character_ActionHandler _actionHandler;
    private Character_EcsBridge _bridge;

    private void Awake()
    {
        _actionHandler = GetComponent<Character_ActionHandler>();
        _bridge = GetComponent<Character_EcsBridge>();
    }

    private void LateUpdate()
    {
        if (_actionHandler == null || _bridge == null) return;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        Entity entity = _bridge.CharacterEntity;
        if (entity == Entity.Null) return;

        EntityManager em = world.EntityManager;
        if (!em.Exists(entity) || !em.HasBuffer<CharacterIncomingHit>(entity)) return;

        DynamicBuffer<CharacterIncomingHit> inbox = em.GetBuffer<CharacterIncomingHit>(entity);
        int count = inbox.Length;
        if (count == 0) return;

        for (int i = 0; i < count; i++)
        {
            CharacterIncomingHit hit = inbox[i];
            ApplyHit((Vector3)hit.SourcePosition, hit.Attack);
        }

        inbox.Clear();
    }

    private void ApplyHit(Vector3 source, NavAgentAttackProfile profile)
    {
        if (!_actionHandler.IsHittable)
            return;

        AttackKnockbackData knockback = new AttackKnockbackData
        {
            type     = profile.KnockbackType,
            force    = profile.KnockbackForce,
            friction = profile.KnockbackFriction
        };
        AttackTimeScaleData hitstop = new AttackTimeScaleData
        {
            duration  = profile.HitstopDuration,
            timeScale = profile.HitstopTimeScale
        };
        AttackDownData down = new AttackDownData
        {
            enabled  = profile.IsDownAttack != 0,
            duration = profile.DownDuration
        };
        AttackLaunchData launch = new AttackLaunchData
        {
            enabled         = profile.LaunchEnabled != 0,
            height          = profile.LaunchHeight,
            suspendDuration = profile.LaunchSuspendDuration
        };

        _actionHandler.ReceiveHit(
            source,
            knockback,
            profile.Damage,
            hitstop,
            down,
            profile.SuperArmorBreak,
            profile.BreakGaugeDamage,
            launch);

        // VFX는 피격자 쪽에서 재생한다. 잡몹 위치(source)는 넉백 방향 계산용이다.
        Vector3 vfxPosition = transform.position;
        Vector3 toAttacker = source - vfxPosition;
        toAttacker.y = 0f;
        if (toAttacker.sqrMagnitude > 0.0001f)
            vfxPosition += toAttacker.normalized * 0.3f;
        vfxPosition.y += HitVfxHeightOffset;
        CombatFeedback.PlayHitFeedback(profile.HitSfx, profile.HitVfxAddress.ToString(), vfxPosition, destroyCancellationToken);
    }
}
