using UnityEngine;

// GameObject(IHitTarget) 공격이 피격자에게 전달하는 히트 결과 묶음.
// 메인 hitbox와 additionalHits(extra)가 동일한 계약으로 결과를 넘기기 위한 통합 구조.
// hitstop/superArmorBreak는 공격 SO 레벨 공유값, knockback/launch/down은 hit별 고유값이다
// (Character_HitboxProcessor.HitContext의 ECS 경로 규칙과 동일).
public readonly struct AttackHitInfo
{
    public readonly AttackKnockbackData Knockback;
    public readonly AttackLaunchData Launch;
    public readonly AttackDownData Down;
    public readonly AttackHitstopData Hitstop;
    public readonly float SuperArmorBreak;

    public AttackHitInfo(
        AttackKnockbackData knockback,
        AttackLaunchData launch,
        AttackDownData down,
        AttackHitstopData hitstop,
        float superArmorBreak)
    {
        Knockback       = knockback;
        Launch          = launch;
        Down            = down;
        Hitstop         = hitstop;
        SuperArmorBreak = superArmorBreak;
    }

    // 메인 hitbox: 결과 전부 SO 최상위 값에서.
    public static AttackHitInfo FromMain(SO_Attack_Data data)
        => new(data.Knockback, data.Launch, data.Down, data.Hitstop, data.SuperArmorBreak);

    // additionalHits: knockback/launch/down은 extra 고유, hitstop/superArmorBreak는 data 공유.
    public static AttackHitInfo FromExtra(SO_Attack_Data data, in AttackExtraHit extra)
        => new(extra.hitResult.knockback, extra.hitResult.launch, extra.hitResult.down, data.Hitstop, data.SuperArmorBreak);
}

public interface IHitTarget
{
    // 지금 타격 대상이 될 수 있는지. false면 공격자(Character_AttackController)가 아예 건너뛴다
    // (ReceiveHit이 무시할 상태 — 사망 연출 중·무적 — 인데도 시체에 타격 연출/히트스톱/게이지가
    // 들어가는 것을 막는다). Vitals 없는 파괴물 등은 항상 true로 두면 된다.
    bool IsHittable { get; }

    // 공중에 떠 있어 hitbox 수직 허용범위를 벗어났더라도 타격 가능한 상태인지(공중 콤보 유지용).
    // 공격자(Character_AttackController)가 이 값을 보고 수직 판정을 완화한다. ECS 잡몹은 시뮬 평면을
    // 유지해 항상 판정에 들어오지만, 실제 물리로 뜨는 캐릭터는 이 플래그로 동등한 체공 피격을 보장한다.
    bool IsAirborneHittable { get; }

    // attackerForward는 Directional 넉백 방향 계산에 쓰인다(없으면 방사형으로 폴백).
    void ReceiveHit(Vector3 attackerPos, Vector3 attackerForward, in AttackHitInfo hit, float finalDamage);
}
