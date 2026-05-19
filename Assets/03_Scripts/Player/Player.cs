using UnityEngine;

[RequireComponent(typeof(Player_Movecontroller))]
[RequireComponent(typeof(Player_ActionHandler))]
[RequireComponent(typeof(Player_Animator))]
[RequireComponent(typeof(Player_Attackcontroller))]
[RequireComponent(typeof(Player_HitboxProcessor))]
[RequireComponent(typeof(CharacterController))]
public class Player : MonoBehaviour
{
    private void OnEnable()
    {
        App.SetInput<InputActions_Move, InputActions_Combat, InputActions_Camera>();
    }

    private void Start()
    {
        App.SetCameraFollow(transform);
        App.SetCombatCameraMode(CombatCameraMode.Tactical, true);
    }

    private void OnDisable()
    {
        App.RemoveInput<InputActions_Move>();
        App.RemoveInput<InputActions_Combat>();
        App.RemoveInput<InputActions_Camera>();
        App.ClearCameraFollow(transform);
    }
}
