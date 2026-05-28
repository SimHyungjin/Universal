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

    public static void PlayCameraCutIn(SkillCutInData data)
        => Main.Camera?.PlayCutIn(data);

    public static void PlayUltimateOverlay(UltimateOverlayData data)
        => Overlay_UltimateActivate.Instance?.Play(data);
}
