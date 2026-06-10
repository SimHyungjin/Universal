using UnityEngine;

// 섹터 점령 시스템의 튜닝 값 모음. plain C# 매니저들(SectorBattleManager/Elite_Manager/Elite_WorldSimulator)은
// 인스펙터가 없으므로, 이 SO를 SectorManager에 끼워 두고 생성 시 주입한다. 디자이너는 이 에셋만 조정하면 된다.
[CreateAssetMenu(fileName = "SO_SectorBattle_Settings", menuName = "Game/Sector Battle Settings")]
public class SO_SectorBattle_Settings : ScriptableObject
{
    [Header("점령 시뮬레이션")]
    [Tooltip("화면 동시 표시 잡몹 총수(진영 비율로 분배)")]
    public int LiveCapTotal = 200;
    [Range(0.5f, 1f), Tooltip("비율이 이 값 이상/이하 = 완전 점령(링크 참여). 0.9면 90% 이상이어야 점령지.")]
    public float CaptureThreshold = 0.9f;

    [Header("점령 안정화 / 배출")]
    [Tooltip("섹터가 100%(완전 점령)에 막 도달한 직후, 압력을 받지 않는 안정화 시간(초)")]
    public float MutationImmunityDuration = 3f;
    [Tooltip("점령 전환 배출 임계(마리). 이만큼 누적될 때까지 모았다가 한 번에 배출한다. 배출 사이 틈이 생겨 플레이어가 100%를 찍을 여지를 만든다.")]
    public int MutationBurstThreshold = 5;

    [Header("전투력 지원 Power (A 통합 압력의 전투력 입력)")]
    [Tooltip("같은 진영 링크 점령지가 전선으로 전달하는 자기 전투 Power 비율. 1이면 100%.")]
    [Range(0f, 1f)]
    public float SupportPowerRatio = 0.2f;
    [Tooltip("지원 Power 거리 감쇠. 1칸(인접)=1배, 2칸=falloff, 3칸=falloff² … 0.5면 한 칸당 절반.")]
    [Range(0f, 1f)]
    public float SupportDistanceFalloff = 0.5f;

    [Header("(A) 통합 점령 압력")]
    [Tooltip("위상(영역 크기=net) vs 전투력(aP-eP) 비중. 0=순수 전투력, 1=순수 위상. 0.5=균형. 허브 붕괴 순간 위상이 전투력 우세를 뒤집는 정도를 결정.")]
    [Range(0f, 1f)]
    public float TopoShare = 0.5f;
    [Tooltip("초당 점령 전환 비율(섹터 총병력 대비). 0.015면 압력 ±1에서 초당 그 섹터 1.5%씩 전환. 병력 규모가 바뀌어도 비율이라 속도가 일관.")]
    public float ConquestFractionPerSec = 0.015f;

    [Header("엘리트 생존 및 연출")]
    [Tooltip("비실체 엘리트가 상대 진영 Power 1당 초당 받는 피해. 0이면 백그라운드에서 피해를 받지 않는다.")]
    public float EliteDamagePerHostilePowerPerSec = 0.03f;
    [Tooltip("실체 엘리트 사망 연출이 보일 시간(초)")]
    public float DeathDisplayDelay = 1.8f;
    [Tooltip("미니맵 교전 배회 반경(적 엘리트가 없을 때 섹터 안을 도는 반경)")]
    public float CombatRoamRadius = 9f;

}
