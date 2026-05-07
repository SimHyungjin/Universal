using UnityEngine;

[RequireComponent(typeof(Player_Movecontroller))]
[RequireComponent(typeof(Player_Animator))]
[RequireComponent(typeof(CharacterController))]
public class Player : MonoBehaviour
{
    private void OnEnable()
    {
        App.SetInput<InputActions_Move>();
        App.SetCameraFollow(transform);
    }

    private void OnDisable()
    {
        App.RemoveInput<InputActions_Move>();
        App.ClearCameraFollow(transform);
    }
}
