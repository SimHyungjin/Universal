using UnityEngine;

// 게임 시작 보드 다이얼(테스트용 — 나중에 UI로 승격). 침식도로 적/아군 섹터 분배를 만들고,
// 진영별 잡몹 구성과 시작 엘리트 로스터를 정의한다.
[CreateAssetMenu(fileName = "SO_Secter_AliveSetting", menuName = "Game/Sector/Game Start Settings")]
public class SO_Secter_AliveSetting : ScriptableObject
{
    [Header("침식도")]
    [Range(0, 9)]
    [Tooltip("0=전부 아군, 9=한 섹터 빼고 적. 적색 섹터 수 = ceil(stage/9 × 섹터수), 플레이어 시작 섹터는 항상 아군으로 남는다.")]
    public int erosionStage = 1;

    [Min(0)]
    [Tooltip("섹터 Capacity가 0(미설정)일 때 쓰는 기본 배경 병력 상한.")]
    public int defaultSectorCapacity = 100;

    [Header("진영 잡몹 구성 (실체화 때만 소비)")]
    [Tooltip("아군 로스터 — 청색 섹터를 채우고, 플레이어 진입 시 이 비율로 실체화.")]
    public SO_Sector_AliveComposition allyComposition;
    [Tooltip("적 로스터 — 적색 섹터를 채우고, 플레이어 진입 시 이 비율로 실체화.")]
    public SO_Sector_AliveComposition enemyComposition;

    [Header("시작 엘리트")]
    [Tooltip("아군 엘리트 — 플레이어 시작 섹터에서 출발(진영은 Ally로 강제).")]
    public EliteSpawnEntry[] allyElites;
    [Tooltip("적 엘리트 — 적 본진(침식 앵커) 한 섹터에서 전원 출발(진영은 Enemy로 강제).")]
    public EliteSpawnEntry[] enemyElites;
}
