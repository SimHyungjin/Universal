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

        CharacterKind ICharacterArchetype.Kind => CharacterKind.Mob;
        string ICharacterArchetype.DisplayName => DisplayName;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public SO_Unit_Stats StatsData => statsData;
        public SO_Actor_AnimationData AnimationData => animationData;
        public SO_ActionRecovery ActionRecovery => actionRecovery;
        public Unit_NavVisualShell VisualPrefab => visualPrefab;
    }
}
