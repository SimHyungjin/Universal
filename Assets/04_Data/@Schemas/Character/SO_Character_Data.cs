using UnityEngine;
using UnityEngine.AddressableAssets;

public enum BattleRole
{
    Vanguard = 0,
    Defender = 1,
    Duelist = 2,
}

// Root character definition: identity, prefab, base stats, default combat setup, and shared feel data.
[CreateAssetMenu(fileName = "SO_Character_Data", menuName = "Game/Character/Character Data")]
public sealed class SO_Character_Data : ScriptableObject, IPrefabCharacterArchetype
{
    [Header("Identity")]
    [SerializeField] private string displayName;
    [SerializeField] private Sprite markerSprite;
    [SerializeField] private BattleRole battleRole = BattleRole.Vanguard;

    [Header("Base Character")]
    [SerializeField] private AssetReferenceGameObject prefab;
    [SerializeField] private SO_Character_Stats statsData;
    [SerializeField] private SO_Character_Loadout defaultLoadout;

    [Header("AI")]
    [Tooltip("이 캐릭터가 AI로 굴 때(엘리트 또는 automode)의 행동·nav·필드 이동 파라미터. " +
             "플레이어 빙의 중엔 Elite_Brain이 꺼져 무시된다. 캐릭터 정체성의 일부(느린 골렘 vs 공격적 듀얼리스트).")]
    [SerializeField] private SO_Elite_Brain aiBrain;

    [Header("Common")]
    [SerializeField] private SO_Actor_AnimationData animationData;
    [SerializeField] private SO_Character_LocomotionFeel locomotionFeel;
    [SerializeField] private SO_WorldPhysics worldPhysics;
    [SerializeField] private SO_Character_InputBuffering inputBuffering;
    [SerializeField] private SO_Character_JumpFeel jumpFeel;
    [SerializeField] private SO_Character_DashRule dashRule;
    [SerializeField] private SO_ActionRecovery actionRecovery;
    [SerializeField] private SO_Character_BreakFeel breakFeel;

    CharacterKind ICharacterArchetype.Kind => CharacterKind.Player;
    string ICharacterArchetype.DisplayName => DisplayName;
    AssetReferenceGameObject IPrefabCharacterArchetype.Prefab => prefab;

    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    public Sprite MarkerSprite => markerSprite;
    public BattleRole BattleRole => battleRole;

    public AssetReferenceGameObject Prefab => prefab;
    public SO_Elite_Brain AiBrain => aiBrain;
    public SO_Character_Stats StatsData => statsData;
    public SO_Character_Loadout DefaultLoadout => defaultLoadout;
    public SO_Actor_AnimationData AnimationData => animationData;
    public SO_Character_LocomotionFeel LocomotionFeel => locomotionFeel;
    public SO_WorldPhysics WorldPhysics => worldPhysics;
    public SO_Character_InputBuffering InputBuffering => inputBuffering;
    public SO_Character_JumpFeel JumpFeel => jumpFeel;
    public SO_Character_DashRule DashRule => dashRule;
    public SO_ActionRecovery ActionRecovery => actionRecovery;
    public SO_Character_BreakFeel BreakFeel => breakFeel;
}
