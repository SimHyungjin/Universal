using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public static class CombatFeedback
{
    public static void PlayHitFeedback(SfxType sfx, string vfxAddress, Vector3 position, CancellationToken token)
    {
        App.PlaySfx(sfx, position);

        if (string.IsNullOrEmpty(vfxAddress)) return;

        SpawnAddressedVfx(vfxAddress, position, token, 0f).Forget();
    }

    public static void SpawnVfxAtPosition(string address, Vector3 position, CancellationToken token, float duration = 0f)
    {
        if (!string.IsNullOrEmpty(address))
            SpawnAddressedVfx(address, position, token, duration).Forget();
    }

    private static async UniTaskVoid SpawnAddressedVfx(string address, Vector3 position, CancellationToken token, float duration)
    {
        var vfx = await App.SpawnAsync<AutoDespawn>(address, token: token);
        if (vfx == null) return;
        vfx.transform.position = position;
        if (duration > 0f)
            vfx.SetDurationAndRestart(duration);
    }
}
