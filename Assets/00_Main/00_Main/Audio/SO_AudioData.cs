using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SO_AudioData", menuName = "Main/AudioData")]
public class SO_AudioData : ScriptableObject
{
    public List<AudioEntry> Bgm = new();
    public List<AudioEntry> Sfx = new();

    public AudioEntry GetBgm(BgmType type)
    {
        int idx = (int)type - 1;
        return idx >= 0 && idx < Bgm.Count ? Bgm[idx] : null;
    }

    public AudioEntry GetSfx(SfxType type)
    {
        int idx = (int)type - 1;
        return idx >= 0 && idx < Sfx.Count ? Sfx[idx] : null;
    }
}
