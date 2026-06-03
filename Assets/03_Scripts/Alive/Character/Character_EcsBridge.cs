using MapNav.Ecs;
using Unity.Entities;
using UnityEngine;

// 캐릭터(플레이어/장수) 위치·진영을 ECS 월드로 전달하는 다리.
// 캐릭터 1명당 CharacterNavTarget 엔티티 1개를 만든다(싱글톤 아님 — 다중 캐릭터 지원).
// CharacterIncomingHit 버퍼도 같은 엔티티에 부착하지만, 드레인은 Character_EcsHitReceiver가 담당한다.
[DisallowMultipleComponent]
public sealed class Character_EcsBridge : MonoBehaviour
{
    [Tooltip("잡몹 공격 판정 시 캐릭터를 점이 아닌 반경으로 취급. 1프레임 stale·넉백 미끄러짐을 흡수.")]
    [SerializeField] private float hitRadius = 0.5f;

    private Character_Vitals _vitals;
    private World _world;
    private Entity _entity = Entity.Null;

    // Character_EcsHitReceiver가 자기 캐릭터의 inbox를 드레인하기 위해 참조한다.
    public Entity CharacterEntity => _entity;

    private void Awake()
    {
        _vitals = GetComponent<Character_Vitals>();
    }

    private void LateUpdate()
    {
        if (!EnsureEntity(out EntityManager em))
            return;

        // Vitals는 Character_ActionHandler.Awake에서 늦게 붙을 수 있어 LateUpdate에서 한 번 더 확인한다.
        if (_vitals == null)
            _vitals = GetComponent<Character_Vitals>();

        // 사망하면 HasValue=0으로 발행한다. 시체가 디스폰되기 전(사망 연출 동안)에도
        // NavTargetingSystem(HasValue==0이면 skip)·NavAttackResolveSystem(HasValue!=0일 때만 타격)이
        // 이 캐릭터를 즉시 타겟·타격·경로 배정에서 제외하도록 한다. ECS 잡몹의 NavAgentDeath.Dying과 동일 역할.
        bool dead = _vitals != null && _vitals.IsDead;

        // 진영이 아직 주입되지 않은 캐릭터(엘리트 실체화 직후 ~ Embodiment.Bind 이전)는 타겟·타격 후보가
        // 아니다. 발행하면 잠정 진영(아군 엘리트가 한 프레임 Enemy로)으로 잡몹이 오인해 몰린다.
        bool active = _vitals != null && !dead && _vitals.FactionResolved;

        em.SetComponentData(_entity, new CharacterNavTarget
        {
            Position  = transform.position,
            HasValue  = (byte)(active ? 1 : 0),
            HitRadius = Mathf.Max(0f, hitRadius),
            Faction   = _vitals != null ? _vitals.Faction : NavFaction.Ally
        });
    }

    private bool EnsureEntity(out EntityManager em)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            _world = null;
            _entity = Entity.Null;
            em = default;
            return false;
        }

        em = world.EntityManager;
        if (_world == world && _entity != Entity.Null && em.Exists(_entity))
            return true;

        _world = world;
        _entity = em.CreateEntity(typeof(CharacterNavTarget));
        em.AddBuffer<CharacterIncomingHit>(_entity);
        return true;
    }

    private void OnDestroy()
    {
        if (_world == null || !_world.IsCreated || _entity == Entity.Null)
            return;

        EntityManager em = _world.EntityManager;
        if (em.Exists(_entity))
            em.DestroyEntity(_entity);
        _entity = Entity.Null;
    }
}
