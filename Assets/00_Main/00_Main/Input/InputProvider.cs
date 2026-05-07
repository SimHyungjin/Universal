using UnityEngine;

public struct MoveInput
{
    public Vector2 Direction;
}

public static class InputProvider
{
    public static MoveInput Move;

    public static void SetMoveDirection(Vector2 direction)
    {
        Move.Direction = Vector2.ClampMagnitude(direction, 1f);
    }

    public static void ResetMove()
    {
        Move.Direction = Vector2.zero;
    }
}
