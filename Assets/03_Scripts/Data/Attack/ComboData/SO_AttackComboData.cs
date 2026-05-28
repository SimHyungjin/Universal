using UnityEngine;

[CreateAssetMenu(fileName = "SO_AttackComboData", menuName = "Game/Combat/Attack Combo Data")]
public sealed class SO_AttackComboData : ScriptableObject
{
    [SerializeField] private SO_AttackData[] attacks;
    [SerializeField, Min(0f)] private float comboWindow = 0.35f;
    [SerializeField] private bool lockMovementDuringComboWindow = true;

    public SO_AttackData[] Attacks => attacks;
    public float ComboWindow => comboWindow;
    public bool LockMovementDuringComboWindow => lockMovementDuringComboWindow;
}
