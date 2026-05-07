using System;
using UnityEngine;

[Serializable]
public class AudioEntry
{
    public string    Name    = "NewEntry";
    public AudioClip Clip;

    [Header("Channel")]
    public AudioChannelType Channel = AudioChannelType.Sound;

    [Header("Playback")]
    [Range(0f, 1f)]  public float Volume   = 1f;
    [Range(-3f, 3f)] public float Pitch    = 1f;
    public int Priority = 128;

    [Header("Loop")]
    public bool  Loop          = false;
    public bool  UseLoopPoint  = false;
    public float LoopStartTime = 0f;

    [Header("Fade")]
    public float FadeInDuration  = 0f;
    public float FadeOutDuration = 0f;

    [Header("3D Spatial")]
    [Range(0f, 1f)] public float SpatialBlend = 0f;
    public float            MinDistance = 1f;
    public float            MaxDistance = 500f;
    public AudioRolloffMode RolloffMode = AudioRolloffMode.Logarithmic;
}
