using UnityEngine;

// 피격 반응 애니메이션의 종류. 캐릭터 기준(풍부)으로 세분화하며, 잡몹은 미보유 종류를
// HitReactionAnimSet의 fallback 체인으로 대체한다.
public enum HitReactionKind
{
    None = 0,
    LightHit,
    HeavyHit,
    Down,
    Launch,
    Wakeup,
    Death
}

// kind별 애니메이션 상태 이름 묶음. 잡몹/캐릭터의 SO_*_AnimationData가 자기 클립으로 채운다.
// 특정 kind 클립이 비어 있으면 Resolve가 fallback 체인으로 대체한다(잡몹엔 launch/down/wakeup 클립이 없을 수 있음).
public readonly struct HitReactionAnimSet
{
    public readonly string LightHit;
    public readonly string HeavyHit;
    public readonly string Down;
    public readonly string Launch;
    public readonly string Wakeup;
    public readonly string Death;
    public readonly float Transition;

    public HitReactionAnimSet(string lightHit, string heavyHit, string down, string launch, string wakeup, string death, float transition)
    {
        LightHit   = lightHit;
        HeavyHit   = heavyHit;
        Down       = down;
        Launch     = launch;
        Wakeup     = wakeup;
        Death      = death;
        Transition = transition;
    }

    // kind에 해당하는 상태 이름. 비어 있으면 fallback 체인을 따른다(없으면 null → 재생 안 함).
    public string Resolve(HitReactionKind kind)
    {
        return kind switch
        {
            HitReactionKind.Death    => Pick(Death),
            HitReactionKind.Wakeup   => Pick(Wakeup) ?? Pick(Down) ?? Pick(HeavyHit) ?? Pick(LightHit),
            HitReactionKind.Launch   => Pick(Launch) ?? Pick(HeavyHit) ?? Pick(LightHit),
            HitReactionKind.Down     => Pick(Down) ?? Pick(HeavyHit) ?? Pick(LightHit),
            HitReactionKind.HeavyHit => Pick(HeavyHit) ?? Pick(LightHit),
            HitReactionKind.LightHit => Pick(LightHit),
            _ => null
        };
    }

    private static string Pick(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

// 피격 애니메이션 "진입·실행"의 단일 진실. 잡몹(Unit_NavVisualShell)과 캐릭터(Character_Animator)가
// kind만 넘기면 동일한 방식(0프레임 강제 재생)으로 클립을 재생한다.
public static class HitReactionPlayer
{
    // kind에 맞는 클립을 0프레임부터 강제 재생한다(잡몹 ForcePlay / 캐릭터 PlayHitReaction 일원화).
    // currentHash는 호출처의 현재 상태 캐시 — 재생한 hash로 갱신한다. 클립이 없으면(fallback도 빈) 아무것도 안 한다.
    public static void Play(Animator animator, in HitReactionAnimSet set, HitReactionKind kind, ref int currentHash)
    {
        if (animator == null) return;
        string state = set.Resolve(kind);
        if (string.IsNullOrWhiteSpace(state)) return;

        int hash = Animator.StringToHash(state);
        currentHash = hash;
        animator.CrossFade(hash, set.Transition, 0, 0f);
    }
}
