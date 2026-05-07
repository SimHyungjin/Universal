using Cysharp.Threading.Tasks;
using UnityEngine;

public enum HapticType { Weak, Soft, Medium, Hard }

public class HapticManager : CoreManager
{
    public void Vibrate(HapticType type)
    {
        switch (type)
        {
            case HapticType.Weak:   Weak();   break;
            case HapticType.Soft:   Soft();   break;
            case HapticType.Medium: Medium(); break;
            case HapticType.Hard:   Hard();   break;
        }
    }

    public void Weak()
    {
#if UNITY_EDITOR
        Debug.Log("[Haptic] Weak");
#elif UNITY_ANDROID
        Vibration.VibrateAndroid(20);
#elif UNITY_IOS
        Vibration.VibrateIOS(ImpactFeedbackStyle.Light);
#endif
    }

    public void Soft()
    {
#if UNITY_EDITOR
        Debug.Log("[Haptic] Soft");
#elif UNITY_ANDROID
        Vibration.VibrateAndroid(30);
#elif UNITY_IOS
        Vibration.VibrateIOS(ImpactFeedbackStyle.Soft);
#endif
    }

    public void Medium()
    {
#if UNITY_EDITOR
        Debug.Log("[Haptic] Medium");
#elif UNITY_ANDROID
        Vibration.VibrateAndroid(60);
#elif UNITY_IOS
        Vibration.VibrateIOS(ImpactFeedbackStyle.Medium);
#endif
    }

    public void Hard()
    {
#if UNITY_EDITOR
        Debug.Log("[Haptic] Hard");
#elif UNITY_ANDROID
        Vibration.VibrateAndroid(100);
#elif UNITY_IOS
        Vibration.VibrateIOS(ImpactFeedbackStyle.Heavy);
#endif
    }
}
