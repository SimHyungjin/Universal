// 전투 수치 계산식의 단일 진실. ATK/DEF 적용 모델이 바뀌면 여기만 수정한다.
// SO들은 raw 값만 들고, 호출처는 이 식만 거친다.
// UnityEngine 의존 없음 — ECS Job/Burst 컨텍스트에서도 안전하게 호출 가능.
public static class CombatFormula
{
    // finalDamage = baseDamage × (1 + attackPower / 100)
    public static float ScaleAttackDamage(float attackPower, float baseDamage)
        => baseDamage * (1f + attackPower * 0.01f);

    // takenDamage = incoming × max(0, 1 - defense / 100)
    public static float ReduceIncomingDamage(float defense, float incoming)
    {
        float mult = 1f - defense * 0.01f;
        if (mult < 0f) mult = 0f;
        return incoming * mult;
    }
}

public static class SectorPowerFormula
{
    public static float Calculate(SO_Character_Data character, float fallbackPower = 0f)
        => Calculate(
            character != null ? character.StatsData : null,
            character != null ? character.DefaultLoadout : null,
            fallbackPower);

    public static float Calculate(
        SO_Character_Stats stats,
        SO_Character_Loadout loadout,
        float fallbackPower = 0f)
        => Calculate(stats, loadout != null ? loadout.EquippedSkills : null, fallbackPower);

    public static float Calculate(
        SO_Character_Stats stats,
        SO_Skill_Data[] equippedSkills,
        float fallbackPower = 0f)
    {
        float power = stats != null ? stats.BaseSectorPower : fallbackPower;

        if (equippedSkills != null)
        {
            for (int i = 0; i < equippedSkills.Length; i++)
                power += equippedSkills[i] != null ? equippedSkills[i].SectorPowerBonus : 0f;
        }

        return power > 0f ? power : 0f;
    }
}
