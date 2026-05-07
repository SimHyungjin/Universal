using UnityEngine;

public struct PlayerNetworkInput
{
    public Vector2 MoveDirection;
}

/// <summary>
/// Game-specific static facade. Keep shared project framework APIs in App/Main.
/// </summary>
public static class Game
{
    public static PlayerNetworkInput CaptureNetworkInput()
    {
        return new PlayerNetworkInput
        {
            MoveDirection = InputProvider.Move.Direction
        };
    }
}
