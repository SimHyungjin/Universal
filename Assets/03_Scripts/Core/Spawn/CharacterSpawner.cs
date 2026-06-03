using System.Threading;
using Cysharp.Threading.Tasks;
using MapNav.Ecs;
using UnityEngine;
using UnityEngine.AddressableAssets;

// 데이터(아키타입)를 받아 월드에 실체화하는 단일 진입점.
// CharacterKind로 realizer를 디스패치한다: 프리팹 종류(Player/Elite)는 GameObject realizer,
// 잡몹은 ECS realizer(NavRuntimeBootstrap)로 위임. ECS와 GameObject를 한 함수로 강제 통합하지 않는다.
public static class CharacterSpawner
{
    // ── GameObject realizer (Player/Elite): 프리팹 한 마리 실체화 ──────────────────
    // AssetReference라 이 호출 시점에만 프리팹이 로드된다. 인스턴스는 Release로 해제해야 한다(특히 장수 실체화/해제 반복).
    public static async UniTask<GameObject> SpawnPrefabAsync(
        IPrefabCharacterArchetype archetype, SpawnRequest request, CancellationToken token = default)
    {
        AssetReferenceGameObject prefabRef = archetype?.Prefab;
        if (prefabRef == null || !prefabRef.RuntimeKeyIsValid())
        {
            Debug.LogWarning($"[CharacterSpawner] '{archetype?.DisplayName}'의 Prefab 참조가 비어 있어 소환할 수 없습니다.");
            return null;
        }

        return await prefabRef.InstantiateAsync(request.Position, request.Rotation)
                              .ToUniTask(cancellationToken: token);
    }

    // SpawnPrefabAsync로 만든 인스턴스를 해제(Addressables 인스턴스 카운트 반환).
    public static void Release(GameObject instance)
    {
        if (instance != null)
            Addressables.ReleaseInstance(instance);
    }

    // 아키타입 종류로 디스패치. 프리팹 종류만 GameObject를 반환한다.
    // 잡몹은 단일 인스턴스가 아니라 대량 ECS라 여기 들어오지 않는다(SpawnMobs 사용).
    public static async UniTask<GameObject> SpawnAsync(
        ICharacterArchetype archetype, SpawnRequest request, CancellationToken token = default)
    {
        switch (archetype?.Kind)
        {
            case CharacterKind.Player:
            case CharacterKind.Elite:
                return await SpawnPrefabAsync((IPrefabCharacterArchetype)archetype, request, token);
            case CharacterKind.Mob:
                Debug.LogWarning("[CharacterSpawner] Mob은 SpawnAsync가 아니라 SpawnMobs(대량 ECS)로 소환합니다.");
                return null;
            default:
                return null;
        }
    }

    // ── ECS realizer (Mob): 기존 NavRuntimeBootstrap에 대량 소환 위임 ───────────────
    // Sector의 mob spawn 명세를 그대로 위임한다. 동적 인구 명세는 추후(빌드 ⑤).
    public static int SpawnMobs(NavAgentSpawnEntry[] entries)
        => NavRuntimeBootstrap.Instance != null ? NavRuntimeBootstrap.Instance.SpawnAgents(entries) : 0;

    public static UniTask SpawnMobsGraduallyAsync(
        NavAgentSpawnEntry[] entries, int batchSize = 3, CancellationToken ct = default)
        => NavRuntimeBootstrap.Instance != null
            ? NavRuntimeBootstrap.Instance.SpawnAgentsGradually(entries, batchSize, ct)
            : UniTask.CompletedTask;
}
