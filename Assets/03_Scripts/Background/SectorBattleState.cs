using MapNav.Ecs;
using UnityEngine;

// 섹터 한 곳의 전투/점령 상태. 백그라운드 전투·실체화 전투·미니맵이 공유하는 단일 진실.
// 진실 단위 = 유닛 수. 제로섬: 총 유닛 수(AllyTotal + EnemyTotal) 합은 고정.
// 변이(진영 전환)·싸움(제로섬 전환)으로만 진영 간 이동한다 — 죽여 없애는 게 아니라 반대 진영이 그만큼 늘어난다.
//  · AllyTotal/EnemyTotal = 진영별 유닛 수(제로섬).
//  · Control/LinkInfluence = 매 틱 RecomputeLinks가 갱신하는 점령 상태와 자기 링크(연결 컴포넌트) 영향력.
//  · 점령 게이지 = AllyTotal / (AllyTotal + EnemyTotal) 비율의 파생.
public sealed class SectorBattleState
{
    public Sector Sector { get; }

    // 비율이 임계(±CaptureThreshold) 도달 시 갱신되는 소유 진영.
    public NavFaction OwnerFaction { get; set; }

    // 점령 상태(매 틱 RecomputeLinks 판정). 완전 점령(Ally/Enemy)만 링크에 참여하고 힘을 전달한다.
    public SectorControl Control { get; set; }
    // 자기가 속한 링크(같은 진영 점령지 연결 컴포넌트)의 영향력 = 그 컴포넌트의 점령 섹터 개수. 경합이면 0.
    public int LinkInfluence { get; set; }
    // 현재 틱의 링크 식별자. 같은 링크의 내부 게이트를 미니맵에서 구분하는 데 쓴다. 경합이면 0.
    public int LinkId { get; set; }
    // 링크를 제거했을 때 영역을 가장 잘 분할하는 대표 섹터.
    public bool IsLinkHub { get; set; }
    // 직전 틱의 허브 여부. 허브 선정 hysteresis가 쓴다 — 직전 허브가 여전히 충분히 좋으면 유지해
    // 허브가 매 틱 인접 칸으로 튀는 진동을 막는다(디펜더가 그 진동을 따라다니지 않게).
    public bool WasLinkHub { get; set; }

    // 100%(완전 점령, 상대 진영 0) 도달 직후 변이 면역 시간. >0이면 변이를 받지 않는다(점령 안정화 유예).
    public float MutationImmunityTimer { get; set; }
    // 직전 틱 완전 점령 여부. false→true로 바뀌는 순간(=막 100% 달성)에만 면역을 부여한다.
    public bool WasFullyControlled { get; set; }
    // 직전 틱 "아군" 완전 점령 여부. 점령 알림(PlayerSectorCaptured)의 엣지 추적용 — WasFullyControlled는
    // 진영 무관이라 적-full→아군-full로 한 번에 뒤집으면 엣지가 안 생겨 누락된다. 아군 점령만 따로 본다.
    public bool WasAllyFull { get; set; }
    // 변이 누적기. |값|이 임계(MutationBurstThreshold)에 도달하면 한 번에 정수만큼 배출한다(연속 아닌 뭉텅이 변이).
    public float MutationAccum { get; set; }

    // 제로섬 총 유닛 수(합 고정).
    public float AllyTotal { get; set; }
    public float EnemyTotal { get; set; }

    public SectorBattleState(Sector sector) => Sector = sector;

    // 그 섹터에 있는 진영별 엘리트 전력 합(매니저가 매 틱 갱신). 잡몹과 달리 가산 가속기로만 작용.
    public float AllyElitePower { get; set; }
    public float EnemyElitePower { get; set; }

    // 전력 = 잡몹 총 병력 + 엘리트 전력. 배경 잠식 "속도"의 기준에만 쓴다(점령도 자체엔 엘리트가 안 들어감).
    public float AllyPower  => Mathf.Max(0f, AllyTotal)  + Mathf.Max(0f, AllyElitePower);
    public float EnemyPower => Mathf.Max(0f, EnemyTotal) + Mathf.Max(0f, EnemyElitePower);

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

}

// 섹터 점령 상태. 완전 점령(Ally/Enemy)만 링크에 참여하고 주변에 힘(변이/지원 Power)을 전달한다.
// 경합(Contested)은 힘을 받기만 하고 전달하지 못해 링크의 단절점이 된다.
public enum SectorControl : byte
{
    Contested = 0,
    Ally = 1,
    Enemy = 2
}
