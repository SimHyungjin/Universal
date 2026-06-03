using UnityEngine;
using UnityEngine.Serialization;

// 섹터 점령 시스템의 튜닝 값 모음. plain C# 매니저들(SectorBattleManager/Elite_Manager/Elite_WorldSimulator)은
// 인스펙터가 없으므로, 이 SO를 SectorManager에 끼워 두고 생성 시 주입한다. 디자이너는 이 에셋만 조정하면 된다.
[CreateAssetMenu(fileName = "SO_SectorBattle_Settings", menuName = "Game/Sector Battle Settings")]
public class SO_SectorBattle_Settings : ScriptableObject
{
    [Header("점령 시뮬레이션")]
    [Tooltip("화면 동시 표시 잡몹 총수(진영 비율로 분배)")]
    public int LiveCapTotal = 200;
    [Tooltip("엘리트 1기의 전력(잡몹 ~N마리 값어치). 배경 잠식 속도 가속에만 쓰임(점령도엔 불참)")]
    public float EliteBasePower = 30f;
    [Tooltip("엘리트가 같은 섹터의 상대 병력 1당 추가로 받는 배경 전력. 광역기가 많은 엘리트가 밀집 병력 상대로 더 강하게 보이도록 한다.")]
    public float ElitePowerBonusPerHostileTotal = 1f;
    [Tooltip("상대 병력 보너스 곡선의 기준 병력. 이 수치에서는 선형 보너스와 같고, 이보다 많으면 더 빠르게 커진다.")]
    public float ElitePowerBonusReferenceTotal = 100f;
    [Tooltip("상대 병력 보너스 곡선 지수. 1=선형, 2=제곱, 3=세제곱.")]
    public float ElitePowerBonusExponent = 2f;
    [Tooltip("상대 병력 비례 보너스 상한. 0.35면 기본 엘리트 전력의 +35%까지만 가산한다.")]
    public float ElitePowerBonusMaxRatio = 0.35f;
    [Tooltip("배경 섹터: 전력차 1당 초당 잠식 병력")]
    public float EncroachRate = 0.05f;
    [Tooltip("배경 잠식 초당 상한")]
    public float EncroachMaxPerSec = 3f;
    [Range(0.5f, 1f), Tooltip("비율이 이 값 이상/이하 = 점령(소유권 확정)")]
    public float CaptureThreshold = 0.95f;

    [Header("Background Pressure")]
    [Tooltip("Effective advantage below this value is treated as stalemate.")]
    public float PressureDeadzone = 0.015f;
    [Tooltip("Effective advantage that maps to full pressure.")]
    public float PressureDecisiveAdvantage = 0.32f;
    [Tooltip("Curve applied while converting advantage to pressure.")]
    public float PressureCurve = 1.25f;
    [Tooltip("How quickly stored pressure fades toward neutral.")]
    public float PressureDecayRate = 0.45f;
    [Tooltip("Stored pressure must exceed this before totals move.")]
    public float PressureMoveThreshold = 0.08f;
    [Tooltip("Curve applied while converting pressure to total movement speed.")]
    public float PressureMoveCurve = 1.15f;
    [Tooltip("How much existing control ratio reinforces the current leader.")]
    public float ControlBiasStrength = 0.18f;
    [Tooltip("Random front drift strength. Lets equal-power sectors actually move.")]
    public float FrontTurbulenceStrength = 0.22f;
    [Tooltip("Power advantage where turbulence fades out.")]
    public float FrontTurbulencePowerFalloff = 0.75f;

    [Header("엘리트")]
    [Tooltip("비실체 엘리트가 적 우세 섹터에서 받는 체력 감소율(우세분 1당 초당). 0이면 백그라운드 불사")]
    public float EliteAttritionRate = 0.03f;
    [Tooltip("실체 엘리트 사망 연출이 보일 시간(초)")]
    public float DeathDisplayDelay = 1.8f;
    [Tooltip("미니맵 교전 배회 반경(적 엘리트가 없을 때 섹터 안을 도는 반경)")]
    public float CombatRoamRadius = 9f;

    [Header("Elite Role Targeting")]
    [Range(0f, 1f)]
    [InspectorName("디펜더 사수 점령률")]
    [Tooltip("디펜더가 자기 진영 점령률이 이 값 이상이 될 때까지 해당 섹터를 지킵니다. 0.8이면 80%까지 사수합니다.")]
    public float DefenderHoldOwnControlRatio = 0.8f;
    [FormerlySerializedAs("RoleHostileControlWeight")]
    [Min(0f)]
    [InspectorName("적 점령률 점수 배율")]
    [Tooltip("목표 섹터 점수에 적 점령률을 더할 때 곱하는 값입니다. 높을수록 적이 많이 점령한 섹터를 더 우선합니다.")]
    public float HostileControlScoreMultiplier = 1000f;
    [FormerlySerializedAs("RoleHostilePowerWeight")]
    [Min(0f)]
    [InspectorName("적 전투력 점수 배율")]
    [Tooltip("목표 섹터 점수에 적 전투력을 더할 때 곱하는 값입니다. 높을수록 적 병력이 센 섹터를 더 우선합니다.")]
    public float HostilePowerScoreMultiplier = 1f;
    [FormerlySerializedAs("RoleDistancePenalty")]
    [Min(0f)]
    [InspectorName("거리 감점 배율")]
    [Tooltip("멀리 있는 섹터 점수에서 거리마다 빼는 값입니다. 높을수록 가까운 섹터를 더 선호합니다.")]
    public float TargetDistanceScorePenalty = 0.01f;
}
