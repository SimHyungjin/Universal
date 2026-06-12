using MapNav.Ecs;
using UnityEngine;

public enum ElitePresenceState
{
    Background,
    Moving,
    Embodied,
    GateExiting,
    GateArriving
}

// 장수 한 마리의 런타임 인스턴스 상태이자 "진실의 원천".
// 섹터 활성 여부와 무관하게 백그라운드에 영속하며, 플레이어가 CurrentSector에 있을 때만
// SO_Elite_Data.VisualPrefab으로 실체화(embody)된다. 미니맵은 이 레코드를 읽어 마커를 그린다.
public sealed class Elite_State : IMinimapTracked
{
    private static int _nextId;

    // 세션 내 안정적 고유 id. 동일 스펙 적장수끼리 서로 추격할 때 대칭을 깨는 데 쓴다
    // (낮은 Id는 멈춰 기다리고 높은 Id가 쫓아가 만남 → 포탈 와리가리 방지).
    public int Id { get; } = _nextId++;

    public SO_Elite_Data Data { get; }
    public Sector HomeSector { get; }
    public ElitePresenceState Presence { get; private set; } = ElitePresenceState.Background;
    public bool IsInTransit => Presence == ElitePresenceState.Moving
                               || Presence == ElitePresenceState.GateExiting
                               || Presence == ElitePresenceState.GateArriving;

    // 현재 소속 섹터. 게이트 통과(또는 백그라운드 매크로 경로의 게이트 홉)로만 바뀐다.
    public Sector CurrentSector { get; set; }

    // 실체화 중인 엘리트가 떠나야 할 목적지 섹터(매크로 역할 로직이 설정). null이 아니면 Elite_Brain이
    // 전투 대신 게이트까지 걸어가 통과 → Elite_Manager.FinalizeGateExit로 매크로 복귀시킨다. 비실체면 무의미.
    public Sector PendingExitSector { get; set; }

    // 매크로 진실 좌표. 실체화 중에는 Elite_Embodiment가 transform(Character_MoveController 구동)에서 미러하고,
    // 비실체일 땐 Elite_WorldSimulator(필드 이동)가 갱신한다.
    public Vector3 WorldPosition { get; set; }
    public Vector3 Forward { get; set; } = Vector3.forward;
    public Sector GateApproachDestinationSector { get; private set; }
    public Sector FieldDestinationSector { get; private set; }
    public bool IsApproachingGate => GateApproachDestinationSector != null;
    public float FieldThinkTimer { get; set; }
    public bool IsFieldTraveling => Presence == ElitePresenceState.Moving;

    // 게이트를 통해 플레이어의 현재 섹터로 막 진입했는지. 실체화(EmbodyAsync) 시 1회 소비해
    // 게이트 대쉬 진입 연출을 재생한다(팝 소환 대신 반대편 게이트에서 통과해 대쉬). 소비 후 false.
    public bool ArrivedViaGate
    {
        get => Presence == ElitePresenceState.GateArriving;
        set
        {
            if (value)
                Presence = ElitePresenceState.GateArriving;
            else if (Presence == ElitePresenceState.GateArriving)
                Presence = Embodiment != null ? ElitePresenceState.Embodied : ElitePresenceState.Background;
        }
    }

    public bool IsGateEntryAnimating
    {
        get => Presence == ElitePresenceState.GateExiting || Presence == ElitePresenceState.GateArriving;
        set
        {
            if (value)
            {
                Presence = Embodiment != null ? ElitePresenceState.GateExiting : ElitePresenceState.GateArriving;
                return;
            }

            if (Presence == ElitePresenceState.GateExiting || Presence == ElitePresenceState.GateArriving)
                Presence = Embodiment != null ? ElitePresenceState.Embodied : ElitePresenceState.Background;
        }
    }

    // 게이트 대쉬 진입의 출발 월드 좌표 = 반대편(출발 섹터) 게이트 지점. 여기서 스폰해 도착 지점(WorldPosition)까지 대쉬.
    public Vector3 GateEntryStart { get; set; }
    public Sector GateArrivalOriginSector { get; private set; }

    // 실체화 대쉬 진입(GateArriving)의 진행도와 도착점. 미니맵은 실체 위치(섹터 밖) 대신 이 스칼라 진행도로
    // "글라이드가 끊긴 위치 → 도착점(도착 섹터 안)"을 보간한다. 섹터 밖 실제 위치를 안 써서 2배 확대와 무관하다.
    public float GateArrivalProgress { get; set; }
    public Vector3 GateArrivalEndPosition { get; set; }

    // GateArriving으로 전환된 순간의 SectorTravelProgress. 미니맵 글라이드 시작점을 도착 게이트가 아니라
    // "글라이드가 멈춘 그 위치"로 잡아 GateApproach 글라이드와 연속시킨다(게이트 통과 시점 실체화 시 점프 방지).
    // 도착 후 실체화되는 기존 경로는 1(=도착 게이트)이라 종전과 동일하게 동작한다.
    public float GateArrivalStartTravelProgress { get; set; }

    // 게이트 접근 + 통과 후 이동을 하나의 연속 진행도(0~1)로 합친다. 미니맵 마커가 통로를 따라 글라이드하는 데 쓴다.
    // 두 구간은 같은 speed로 움직이므로 전체 시간(=거리) 대비 경과로 계산해야 글라이드 속도가 균일하다.
    public float SectorTravelProgress
        => _journeyTotalDuration > 0f ? Mathf.Clamp01(_journeyElapsed / _journeyTotalDuration) : 0f;

    public float Health { get; set; }
    public bool IsAlive => Health > 0f;

    // 사망(Health<=0) 후 경과 시간. Elite_Manager가 매 프레임 누적해 사망 연출 시간이 지나면 수거(Unregister)한다.
    // 디스폰을 실체(GameObject)의 취소 가능한 코루틴에 묶지 않아, 섹터 전환으로 몸체가 먼저 해제돼도 누수되지 않는다.
    public float DeathElapsed { get; set; }

    // 진영: 같은 장수 아키타입이라도 팀(Ally)일 수도 적(Enemy)일 수도 있어 인스턴스 속성으로 둔다.
    public NavFaction Faction { get; }

    // 현재 섹터에서 실체화된 몸체. 비실체 상태면 null. Elite_Manager만 관리하는 런타임 전용 핸들.
    public GameObject Embodiment { get; private set; }

    // 매크로 의도(목표 섹터/순찰 경로 등)는 ④ BackgroundSimulator + Brain 단계에서 추가된다.

    public Elite_State(SO_Elite_Data data, Sector sector, Vector3 worldPosition, Vector3 forward, NavFaction faction)
    {
        Data          = data;
        HomeSector    = sector;
        CurrentSector = sector;
        WorldPosition = worldPosition;
        Forward       = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        Faction       = faction;
        Health        = data != null && data.Character != null && data.Character.StatsData != null
                            ? data.Character.StatsData.MaxHealth
                            : 1f;
    }

    // startJourney=false면 게이트 접근 직후 이어붙이는 호출이라 여정 타이머(글라이드 진행도)를 리셋하지 않는다.
    public void BeginFieldTravel(Sector destination, Vector3 endPosition, float duration, bool startJourney = true)
    {
        if (destination == null || destination == CurrentSector)
            return;

        Presence = ElitePresenceState.Moving;
        FieldDestinationSector = destination;
        _fieldTravelStartPosition = WorldPosition;
        _fieldTravelEndPosition = endPosition;
        _fieldTravelDuration = Mathf.Max(0.01f, duration);
        _fieldTravelElapsed = 0f;

        if (startJourney)
        {
            _journeyElapsed = 0f;
            _journeyTotalDuration = _fieldTravelDuration; // 접근 없이 바로 이동: 여정=이동 구간만.
        }

        Vector3 forward = endPosition - WorldPosition;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.0001f)
            Forward = forward.normalized;
    }

    public void BeginGateApproach(
        Sector destination,
        Vector3 gatePosition,
        Vector3 travelEndPosition,
        float approachDuration,
        float travelDuration)
    {
        if (destination == null || destination == CurrentSector)
            return;

        Presence = ElitePresenceState.Moving;
        GateApproachDestinationSector = destination;
        _gateApproachStartPosition = WorldPosition;
        _gateApproachEndPosition = gatePosition;
        _gateApproachDuration = Mathf.Max(0.01f, approachDuration);
        _gateApproachElapsed = 0f;
        _pendingFieldTravelEndPosition = travelEndPosition;
        _pendingFieldTravelDuration = Mathf.Max(0.01f, travelDuration);

        // 여정 = 접근 + 이동 전체 시간. 글라이드 진행도를 이 합으로 나눠 속도를 균일하게.
        _journeyElapsed = 0f;
        _journeyTotalDuration = _gateApproachDuration + _pendingFieldTravelDuration;

        Vector3 forward = gatePosition - WorldPosition;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.0001f)
            Forward = forward.normalized;
    }

    public bool TickGateApproach(float deltaTime)
    {
        if (GateApproachDestinationSector == null)
            return false;

        _gateApproachElapsed += Mathf.Max(0f, deltaTime);
        _journeyElapsed += Mathf.Max(0f, deltaTime);
        float t = Mathf.Clamp01(_gateApproachElapsed / _gateApproachDuration);
        WorldPosition = Vector3.Lerp(_gateApproachStartPosition, _gateApproachEndPosition, t);

        if (t < 1f)
            return false;

        Sector destination = GateApproachDestinationSector;
        Vector3 travelEndPosition = _pendingFieldTravelEndPosition;
        float travelDuration = _pendingFieldTravelDuration;

        GateApproachDestinationSector = null;
        _gateApproachElapsed = 0f;
        _gateApproachDuration = 0f;
        WorldPosition = _gateApproachEndPosition;
        // startJourney=false: 여정 타이머(_journeyElapsed/Total)를 리셋하지 않고 이어서 이동 구간으로 넘어간다.
        BeginFieldTravel(destination, travelEndPosition, travelDuration, startJourney: false);
        return true;
    }

    public bool TickFieldTravel(float deltaTime)
    {
        if (FieldDestinationSector == null)
            return false;

        _fieldTravelElapsed += Mathf.Max(0f, deltaTime);
        _journeyElapsed += Mathf.Max(0f, deltaTime);
        float t = Mathf.Clamp01(_fieldTravelElapsed / _fieldTravelDuration);
        WorldPosition = Vector3.Lerp(_fieldTravelStartPosition, _fieldTravelEndPosition, t);

        if (t < 1f)
            return false;

        CurrentSector = FieldDestinationSector;
        FieldDestinationSector = null;
        WorldPosition = _fieldTravelEndPosition;
        Presence = ElitePresenceState.Background;
        return true;
    }

    public void CancelFieldTravel()
    {
        GateApproachDestinationSector = null;
        FieldDestinationSector = null;
        _gateApproachElapsed = 0f;
        _gateApproachDuration = 0f;
        _fieldTravelElapsed = 0f;
        _fieldTravelDuration = 0f;
        _journeyElapsed = 0f;
        _journeyTotalDuration = 0f;
        Presence = Embodiment != null ? ElitePresenceState.Embodied : ElitePresenceState.Background;
    }

    public void AttachEmbodiment(GameObject embodiment)
    {
        Embodiment = embodiment;
        if (Presence != ElitePresenceState.GateArriving && Presence != ElitePresenceState.GateExiting)
            Presence = ElitePresenceState.Embodied;
    }

    public void DetachEmbodiment()
    {
        Embodiment = null;
        if (Presence == ElitePresenceState.Embodied || Presence == ElitePresenceState.GateExiting)
            Presence = ElitePresenceState.Background;
    }

    public void BeginEmbodiedGateExit(Sector destination)
    {
        PendingExitSector = destination;
    }

    public void BeginEmbodiedGateExitDash(Sector destination)
    {
        PendingExitSector = destination;
        if (destination != null)
            Presence = ElitePresenceState.GateExiting;
    }

    public void CancelEmbodiedGateExit()
    {
        PendingExitSector = null;
        if (Presence == ElitePresenceState.GateExiting)
            Presence = Embodiment != null ? ElitePresenceState.Embodied : ElitePresenceState.Background;
    }

    public void BeginGateArrival(Sector from, Vector3 gateEntryStart)
    {
        GateArrivalOriginSector = from;
        GateEntryStart = gateEntryStart;
        GateArrivalProgress = 0f; // EmbodyAsync(도착점 세팅) 전이라도 0이면 마커가 시작점에 머문다.
        GateArrivalStartTravelProgress = SectorTravelProgress; // 전환 시점 글라이드 위치(도착 경로면 1=도착 게이트).
        Presence = ElitePresenceState.GateArriving;
    }

    public void FinishGateArrival()
    {
        if (Presence == ElitePresenceState.GateArriving)
            Presence = Embodiment != null ? ElitePresenceState.Embodied : ElitePresenceState.Background;
        GateArrivalOriginSector = null;
    }

    private Vector3 _gateApproachStartPosition;
    private Vector3 _gateApproachEndPosition;
    private Vector3 _pendingFieldTravelEndPosition;
    private float _gateApproachElapsed;
    private float _gateApproachDuration;
    private float _pendingFieldTravelDuration;
    private Vector3 _fieldTravelStartPosition;
    private Vector3 _fieldTravelEndPosition;
    private float _fieldTravelElapsed;
    private float _fieldTravelDuration;

    // 접근+이동을 아우르는 여정 타이머. SectorTravelProgress(미니맵 글라이드 진행도)가 이 둘로 균일 속도를 만든다.
    private float _journeyElapsed;
    private float _journeyTotalDuration;

    // IMinimapTracked: 미니맵은 CurrentSector 노드로 직접 투영해 방 모양 안에 마커를 그린다.
    Sector IMinimapTracked.Sector => CurrentSector;
    Vector3 IMinimapTracked.WorldPosition => WorldPosition;
    Vector3 IMinimapTracked.Forward => Forward;

    bool IMinimapTracked.TryGetTransition(out Sector from, out Sector to, out float t)
    {
        from = CurrentSector;
        to   = Presence == ElitePresenceState.GateExiting ? PendingExitSector : null;
        t    = 0f;
        if (to != null)
            return true;

        // GateArriving(실체화 대쉬 진입 연출)은 글라이드 transition을 만들지 않는다. 실체가 게이트→도착점으로
        // 직접 대쉬하고 WorldPosition이 그걸 미러하므로, 일반 마커가 실체 위치를 따라가게 둔다(ShouldHideEliteMarker에서도 제외).
        // 도착 섹터 노드 투영 시 출발 게이트는 가장자리(도착 게이트)로 clamp돼 백그라운드 글라이드 끝과 연속된다.

        // 게이트 접근 + 통과 후 이동은 같은 출발→목표 라우트라 하나의 연속 진행도로 합쳐 한 번만 글라이드.
        Sector destination = GateApproachDestinationSector != null
            ? GateApproachDestinationSector
            : FieldDestinationSector;
        if (destination != null)
        {
            to = destination;
            t  = SectorTravelProgress;
            return true;
        }

        return false;
    }
}
