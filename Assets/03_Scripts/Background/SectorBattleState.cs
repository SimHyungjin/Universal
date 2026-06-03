using MapNav.Ecs;
using UnityEngine;

// 섹터 한 곳의 전투/점령 상태. 백그라운드 전투·실체화 전투·미니맵이 공유하는 단일 진실.
// 제로섬 모델: 총 병력(AllyTotal + EnemyTotal) 합은 고정. 한 진영을 줄이면 반대 진영이 그만큼 늘어난다(영역 물들이기).
//  · AllyTotal/EnemyTotal = 진영별 총 병력(제로섬). 죽임/잠식으로만 진영 간 이동한다.
//  · LiveCount = 화면(실체화)에 나와 있는 실제 수. LiveCap을 비율 분배한 만큼만 보인다(Total의 표시일 뿐).
//  · 점령 게이지 = AllyTotal / (AllyTotal + EnemyTotal) 비율의 파생(별도 누적/튜닝 없음).
public sealed class SectorBattleState
{
    public Sector Sector { get; }

    // 비율이 임계(±0.95) 도달 시 갱신되는 소유 진영.
    public NavFaction OwnerFaction { get; set; }

    // 제로섬 총 병력(합 고정).
    public float AllyTotal { get; set; }
    public float EnemyTotal { get; set; }

    // 화면에 나와 있는 실제 NavAgent 수(폴링이 갱신). 백그라운드 섹터에선 0.
    public int AllyLiveCount { get; set; }
    public int EnemyLiveCount { get; set; }

    public SectorBattleState(Sector sector) => Sector = sector;

    // 그 섹터에 있는 진영별 엘리트 전력 합(매니저가 매 틱 갱신). 잡몹과 달리 가산 가속기로만 작용.
    public float AllyElitePower { get; set; }
    public float EnemyElitePower { get; set; }
    public float AllyEliteAttritionPower { get; set; }
    public float EnemyEliteAttritionPower { get; set; }
    public float ControlPressure { get; set; }

    // 전력 = 잡몹 총 병력 + 엘리트 전력. 배경 잠식 "속도"의 기준에만 쓴다(점령도 자체엔 엘리트가 안 들어감).
    public float AllyPower  => Mathf.Max(0f, AllyTotal)  + Mathf.Max(0f, AllyElitePower);
    public float EnemyPower => Mathf.Max(0f, EnemyTotal) + Mathf.Max(0f, EnemyElitePower);
    public float AllyAttritionPower  => Mathf.Max(0f, AllyTotal)  + Mathf.Max(0f, AllyEliteAttritionPower);
    public float EnemyAttritionPower => Mathf.Max(0f, EnemyTotal) + Mathf.Max(0f, EnemyEliteAttritionPower);

    // 잡몹 총 병력 합(화면 표시 분배용 — 엘리트는 별도 마커라 제외).
    public float TotalSum => Mathf.Max(0f, AllyTotal) + Mathf.Max(0f, EnemyTotal);

    // 점령 비율(0=적 점령, 1=아군 점령, 0.5=중립). 잡몹 병력 비율만 — 잡몹을 다 잡으면 엘리트가 남아도 완전 점령된다.
    public float GaugeNormalized
    {
        get
        {
            float total = TotalSum;
            return total > 0f ? Mathf.Clamp01(Mathf.Max(0f, AllyTotal) / total) : 0.5f;
        }
    }

    public float TotalOf(NavFaction faction) => faction == NavFaction.Ally ? AllyTotal : EnemyTotal;

    public void AddTotal(NavFaction faction, float delta)
    {
        if (faction == NavFaction.Ally) AllyTotal = Mathf.Max(0f, AllyTotal + delta);
        else EnemyTotal = Mathf.Max(0f, EnemyTotal + delta);
    }

    public int LiveOf(NavFaction faction) => faction == NavFaction.Ally ? AllyLiveCount : EnemyLiveCount;

    public void SetLive(NavFaction faction, int value)
    {
        value = Mathf.Max(0, value);
        if (faction == NavFaction.Ally) AllyLiveCount = value;
        else EnemyLiveCount = value;
    }
}
