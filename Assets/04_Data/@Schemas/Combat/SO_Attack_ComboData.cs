using UnityEngine;

[CreateAssetMenu(fileName = "SO_Attack_ComboData", menuName = "Game/Combat/Attack Combo Data")]
public sealed class SO_Attack_ComboData : ScriptableObject
{
    [SerializeField] private SO_Attack_Data[] attacks;
    [SerializeField, Min(0f)] private float comboWindow = 0.35f;
    [SerializeField] private bool lockMovementDuringComboWindow = true;

    public SO_Attack_Data[] Attacks => attacks;
    public float ComboWindow => comboWindow;
    public bool LockMovementDuringComboWindow => lockMovementDuringComboWindow;
}
