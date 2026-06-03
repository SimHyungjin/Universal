using UnityEngine;
using UnityEngine.AddressableAssets;

// 장수(엘리트) 아키타입 = 캐릭터(SO_Character_Data) + AI 래퍼.
// 전투 스탯/애니/feel/콤보/프리팹/마커는 참조한 SO_Character_Data에서 오고, 여기서는 AI/필드 파라미터만 덧댄다.
// character는 플레이어용 SO_Character_Data를 그대로 가리켜도 되고(외형/스탯 동일), 전용으로 둬도 된다.
[CreateAssetMenu(fileName = "SO_Elite_Data", menuName = "Game/Elite/Elite Data")]
public sealed class SO_Elite_Data : ScriptableObject, IPrefabCharacterArchetype
{
    [Header("Character (재사용 — 스탯/애니/feel/콤보/프리팹/마커)")]
    [SerializeField] private SO_Character_Data character;

    [Header("Elite-only")]
    [Tooltip("AI 행동 + nav + 필드/매크로 파라미터.")]
    [SerializeField] private SO_Elite_Brain _brain;

    CharacterKind ICharacterArchetype.Kind => CharacterKind.Elite;
    string ICharacterArchetype.DisplayName => character != null ? character.DisplayName : name;
    AssetReferenceGameObject IPrefabCharacterArchetype.Prefab => character != null ? character.Prefab : null;

    public SO_Character_Data Character => character;
    public SO_Elite_Brain    Brain     => _brain;

    // 미니맵 마커는 캐릭터 정체성의 일부라 SO_Character_Data가 보유한다(여기선 위임만).
    public Sprite MarkerSprite => character != null ? character.MarkerSprite : null;
}
