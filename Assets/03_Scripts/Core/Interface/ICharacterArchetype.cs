using UnityEngine.AddressableAssets;

// 캐릭터 데이터 SO의 공통 계약. CharacterSpawner가 Kind로 realizer를 디스패치한다.
// player/elite는 프리팹으로, mob은 ECS로 실체화되지만 "데이터를 주면 소환된다"는 진입점은 하나다.
public enum CharacterKind
{
    Player,
    Elite,
    Mob,
}

// 모든 캐릭터 데이터 SO가 구현. 종류 식별 + 표시 이름.
public interface ICharacterArchetype
{
    CharacterKind Kind { get; }
    string DisplayName { get; }
}

// 프리팹으로 실체화되는 캐릭터(Player/Elite). 데이터에 프리팹 참조를 드래그하되,
// AssetReference라 실제 메모리 로드는 InstantiateAsync 시점에만 일어난다(SO는 항상 가볍게, 프리팹은 지연 로드 → 장수 스트리밍 유지).
public interface IPrefabCharacterArchetype : ICharacterArchetype
{
    AssetReferenceGameObject Prefab { get; }
}
