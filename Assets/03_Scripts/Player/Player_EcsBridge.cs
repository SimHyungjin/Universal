using MapNav.Ecs;
using Unity.Entities;
using UnityEngine;

// 플레이어 위치를 ECS 월드로 전달하는 다리.
// PlayerIncomingHit 버퍼도 같은 싱글톤에 부착하지만, 드레인은 Player_HitReceiver가 담당한다.
[DisallowMultipleComponent]
public sealed class Player_EcsBridge : MonoBehaviour
{
    [Tooltip("잡몹 공격 판정 시 플레이어를 점이 아닌 반경으로 취급. 1프레임 stale·넉백 미끄러짐을 흡수.")]
    [SerializeField] private float hitRadius = 0.5f;

    private World _world;
    private Entity _singleton = Entity.Null;

    private void LateUpdate()
    {
        if (!EnsureSingleton(out EntityManager em))
            return;

        em.SetComponentData(_singleton, new PlayerNavTarget
        {
            Position = transform.position,
            HasValue = 1,
            HitRadius = Mathf.Max(0f, hitRadius)
        });
    }

    private bool EnsureSingleton(out EntityManager em)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            _world = null;
            _singleton = Entity.Null;
            em = default;
            return false;
        }

        em = world.EntityManager;
        if (_world == world && _singleton != Entity.Null && em.Exists(_singleton))
            return true;

        _world = world;
        _singleton = em.CreateEntity(typeof(PlayerNavTarget));
        em.AddBuffer<PlayerIncomingHit>(_singleton);
        return true;
    }

    private void OnDestroy()
    {
        if (_world == null || !_world.IsCreated || _singleton == Entity.Null)
            return;

        EntityManager em = _world.EntityManager;
        if (em.Exists(_singleton))
            em.DestroyEntity(_singleton);
        _singleton = Entity.Null;
    }
}
