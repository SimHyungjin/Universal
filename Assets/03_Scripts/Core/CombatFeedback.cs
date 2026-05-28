using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public static class CombatFeedback
{
    public static void PlayHitFeedback(SO_AttackData data, Vector3 position, CancellationToken token)
    {
        if (data == null) return;
        PlayHitFeedback(data.HitSfx, data.HitVfxAddress, position, token);

        AttackCameraShakeData shake = data.CameraShake;
        if (shake.enabled)
            App.ShakeCamera(shake.amplitude, shake.duration, shake.frequency);
    }

    public static void PlayHitFeedback(SfxType sfx, string vfxAddress, Vector3 position, CancellationToken token)
    {
        App.PlaySfx(sfx, position);

        if (string.IsNullOrEmpty(vfxAddress)) return;

        SpawnAddressedVfx(vfxAddress, position, token).Forget();
    }

    public static void SpawnVfxAtPosition(string address, Vector3 position, CancellationToken token)
    {
        if (!string.IsNullOrEmpty(address))
            SpawnAddressedVfx(address, position, token).Forget();
    }

    private static async UniTaskVoid SpawnAddressedVfx(string address, Vector3 position, CancellationToken token)
    {
        var vfx = await App.SpawnAsync<AutoDespawn>(address, token: token);
        if (vfx != null)
            vfx.transform.position = position;
    }
}
