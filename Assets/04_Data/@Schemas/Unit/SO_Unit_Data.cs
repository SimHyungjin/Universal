using UnityEngine;
using UnityEngine.Serialization;

namespace MapNav.Ecs
{
    [CreateAssetMenu(fileName = "SO_Unit_Data", menuName = "Game/Unit/Unit Data")]
    public sealed class SO_Unit_Data : ScriptableObject, ICharacterArchetype
    {
        [Header("Identity")]
        [SerializeField] private string displayName;

        [Header("Data")]
        [SerializeField] private SO_Unit_Stats statsData;
        [SerializeField] private SO_Actor_AnimationData animationData;
        [SerializeField] private SO_ActionRecovery actionRecovery;

        [Header("Visual Shell")]
        [FormerlySerializedAs("enemyVisualPrefab")]
        [SerializeField] private Unit_NavVisualShell visualPrefab;

        [Header("Faction Visual")]
        [Tooltip("진영별 본체 머테리얼. 스폰 시 진영색을 입히고, 전향 순간 교체한다.")]
        [SerializeField] private Material allyMaterial;
        [SerializeField] private Material enemyMaterial;
        [Tooltip("적↔아군 전향 순간 그 자리에 터지는 파티클의 Addressable 주소(죽음→부활/변이 연출). 풀에서 스폰되어 자동 회수된다.")]
        [SerializeField] private string conversionVfxAddress;

        CharacterKind ICharacterArchetype.Kind => CharacterKind.Mob;
        string ICharacterArchetype.DisplayName => DisplayName;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public SO_Unit_Stats StatsData => statsData;
        public SO_Actor_AnimationData AnimationData => animationData;
        public SO_ActionRecovery ActionRecovery => actionRecovery;
        public Unit_NavVisualShell VisualPrefab => visualPrefab;
        public Material AllyMaterial => allyMaterial;
        public Material EnemyMaterial => enemyMaterial;
        public string ConversionVfxAddress => conversionVfxAddress;

        // 진영에 맞는 본체 머테리얼. Bind 시 색 입히기·전향 시 교체에 공용.
        public Material MaterialFor(NavFaction faction)
            => faction == NavFaction.Ally ? allyMaterial : enemyMaterial;
    }
}
