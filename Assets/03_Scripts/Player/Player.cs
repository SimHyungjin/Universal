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
        Vector3 offset = new Vector3(5f, 11f, -5f);

        App.SetCameraView( transform.position + offset, new Vector3(55f, -45f, 0f), orthographicSize: 3f);
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
