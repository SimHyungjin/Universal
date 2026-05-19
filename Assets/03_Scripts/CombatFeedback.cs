using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public static class CombatFeedback
{
    public static void PlayHitFeedback(SO_AttackData data, Vector3 position, CancellationToken token)
    {
        if (data == null) return;

        App.PlaySfx(data.HitSfx, position);

        if (string.IsNullOrEmpty(data.HitVfxAddress)) return;

        SpawnAddressedVfx(data.HitVfxAddress, position, token).Forget();
    }

    private static async UniTaskVoid SpawnAddressedVfx(string address, Vector3 position, CancellationToken token)
    {
        var vfx = await App.SpawnAsync<AutoDespawn>(address, token: token);
        if (vfx != null)
            vfx.transform.position = position;
    }
}
