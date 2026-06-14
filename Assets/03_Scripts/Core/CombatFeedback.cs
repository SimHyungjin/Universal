using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public static class CombatFeedback
{
    // 적중 SFX/VFX. 진영·공격자 무관 — 적/아군 AI 공격도 그대로 재생한다(연출이지 플레이어 시점 효과가 아님).
    public static void PlayHitFeedback(SO_Attack_Data data, Vector3 position, CancellationToken token)
    {
        if (data == null) return;
        PlayHitFeedback(data.HitSfx, data.HitVfxAddress, position, token);
    }

    // 카메라 셰이크는 "플레이어 시점" 효과라 SFX/VFX와 분리한다. 호출부(공격 경로)가
    // PlayerController.IsLocalPlayer로 게이트해 로컬 플레이어 공격에만 부른다.
    public static void PlayHitCameraShake(SO_Attack_Data data)
    {
        if (data == null) return;
        AttackCameraShakeData shake = data.CameraShake;
        if (shake.enabled)
            App.ShakeCamera(shake.amplitude, shake.duration, shake.frequency);
    }

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
