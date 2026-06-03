using UnityEngine;

[RequireComponent(typeof(Character_MoveController))]
[RequireComponent(typeof(Character_ActionHandler))]
[RequireComponent(typeof(Character_Animator))]
[RequireComponent(typeof(Character_AttackController))]
[RequireComponent(typeof(Character_HitboxProcessor))]
[RequireComponent(typeof(Player_InputCommandSource))]
[RequireComponent(typeof(CharacterController))]
public class Player_Actor : MonoBehaviour
{
    private void OnEnable()
    {
        App.SetInput<InputActions_Move, InputActions_Combat, InputActions_Camera>();
    }

    private void Start()
    {
        Vector3 offset = new Vector3(5f, 11f, -5f);

        App.SetCameraView( transform.position + offset, new Vector3(55f, -45f, 0f), orthographicSize: 5f);
        App.SetCameraFollow(transform, offset, snap: true);
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
