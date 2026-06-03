using System;
using UnityEngine;

[Serializable]
public struct MinimapMarkerSettings
{
    [SerializeField] private Sprite sprite;
    [SerializeField] private Color color;
    [SerializeField] private float worldSize;
    [SerializeField] private float minScreenPx;
    [SerializeField] private float maxScreenPx;
    [SerializeField] private bool rotateWithTarget;

    public MinimapMarkerSettings(
        Sprite sprite,
        Color color,
        float worldSize,
        float minScreenPx = 10f,
        float maxScreenPx = 48f,
        bool rotateWithTarget = false)
    {
        this.sprite = sprite;
        this.color = color;
        this.worldSize = worldSize;
        this.minScreenPx = minScreenPx;
        this.maxScreenPx = maxScreenPx;
        this.rotateWithTarget = rotateWithTarget;
    }

    public Sprite Sprite => sprite;
    public Color Color => color.a > 0f ? color : Color.white;
    public float WorldSize => worldSize > 0f ? worldSize : 30f;
    public float MinScreenPx => minScreenPx > 0f ? minScreenPx : 10f;
    public float MaxScreenPx => maxScreenPx > 0f ? Mathf.Max(maxScreenPx, MinScreenPx) : 48f;
    public bool RotateWithTarget => rotateWithTarget;

    public MinimapMarkerSettings WithSprite(Sprite overrideSprite)
        => new(overrideSprite != null ? overrideSprite : sprite, Color, WorldSize, MinScreenPx, MaxScreenPx, RotateWithTarget);

    public static MinimapMarkerSettings DefaultPlayer
        => new(null, new Color(0.2f, 1f, 0.35f, 1f), 60f, 10f, 48f, true);

    public static MinimapMarkerSettings DefaultElite
        => new(null, new Color(1f, 0.3f, 0.25f, 1f), 24f, 8f, 28f, true);

    public static MinimapMarkerSettings DefaultMob
        => new(null, new Color(1f, 0.3f, 0.25f, 1f), 18f, 6f, 22f, false);
}
