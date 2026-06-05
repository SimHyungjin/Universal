using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MapNav.Ecs;
using Unity.Entities;
using UnityEngine;

// 발사체/장판에 주입되는 공격자 컨텍스트. 진영·ECS 엔티티(강제 어그로)와
// 명중 시 공격자 의존 효과(흡혈·게이지)에 필요한 핸들러·게이지 계수를 함께 넘긴다.
public readonly struct RangedOwner
{
    public readonly NavFaction Faction;
    public readonly Entity Entity;
    public readonly Character_ActionHandler Handler;
    public readonly float GaugeGainPerDamage;

    public RangedOwner(NavFaction faction, Entity entity, Character_ActionHandler handler, float gaugeGainPerDamage)
    {
        Faction = faction;
        Entity = entity;
        Handler = handler;
        GaugeGainPerDamage = gaugeGainPerDamage;
    }
}

// 명중 시 공통 효과. 근접(Character_AttackController)·발사체(Projectile_Hitbox)·장판(Field_Hitbox)이 공유한다.
public static class CombatOnHit
{
    // 공격자 의존 효과: 흡혈 + 게이지. handler가 null(공격자 사망/소멸)이면 건너뛴다.
    public static void ApplyAttackerGains(SO_Attack_Data data, float finalDamage, Character_ActionHandler handler, float gaugeGainPerDamage)
    {
        if (handler == null) return;

        if (data.LifeSteal.enabled)
        {
            float heal = finalDamage * data.LifeSteal.ratio;
            if (data.LifeSteal.maxPerHit > 0f)
                heal = Mathf.Min(heal, data.LifeSteal.maxPerHit);
            handler.Heal(heal);
        }

        handler.AddGauge(finalDamage * gaugeGainPerDamage);
    }

    // 히트 트리거 카메라 컷인(전역). cue.trigger == Hit일 때만 발동.
    public static void PlayHitCameraCue(SO_Attack_Data data)
    {
        AttackCameraCueData cue = data.CameraCue;
        if (!cue.enabled || cue.trigger != AttackCueTrigger.Hit) return;

        Game.PlayCameraCutIn(new SkillCutInData
        {
            enabled = true,
            duration = cue.duration > 0f ? cue.duration : Mathf.Max(0.01f, data.Duration),
            fovOverride = cue.fovOverride,
            distanceOverride = cue.distanceOverride,
            heightDelta = cue.heightDelta,
            yawVelocity = cue.yawVelocity
        });
    }

    // 히트스톱(전역 시간 감속). 근접/발사체/장판 공통.
    public static async UniTaskVoid TriggerHitstop(AttackHitstopData hitstop, CancellationToken token)
    {
        if (hitstop.duration <= 0f) return;

        Main.Loop.SetGameSpeed(hitstop.timeScale);
        await UniTask.Delay(
            TimeSpan.FromSeconds(hitstop.duration),
            ignoreTimeScale: true,
            cancellationToken: token);
        if (Main.Loop != null)
            Main.Loop.SetGameSpeed(1f);
    }
}
