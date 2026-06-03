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

    // 게이트 이동 진행도(0~1). 미니맵 마커가 통로(엣지)를 따라 글라이드하는 데 쓴다.
    public float FieldTravelProgress
        => _fieldTravelDuration > 0f ? Mathf.Clamp01(_fieldTravelElapsed / _fieldTravelDuration) : 0f;

    public float GateApproachProgress
        => _gateApproachDuration > 0f ? Mathf.Clamp01(_gateApproachElapsed / _gateApproachDuration) : 0f;

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

    public void BeginFieldTravel(Sector destination, Vector3 endPosition, float duration)
    {
        if (destination == null || destination == CurrentSector)
            return;

        Presence = ElitePresenceState.Moving;
        FieldDestinationSector = destination;
        _fieldTravelStartPosition = WorldPosition;
        _fieldTravelEndPosition = endPosition;
        _fieldTravelDuration = Mathf.Max(0.01f, duration);
        _fieldTravelElapsed = 0f;

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
        BeginFieldTravel(destination, travelEndPosition, travelDuration);
        return true;
    }

    public bool TickFieldTravel(float deltaTime)
    {
        if (FieldDestinationSector == null)
            return false;

        _fieldTravelElapsed += Mathf.Max(0f, deltaTime);
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

        if (Presence == ElitePresenceState.GateArriving && GateArrivalOriginSector != null)
        {
            from = GateArrivalOriginSector;
            to = CurrentSector;
            t = 0f;
            return to != null;
        }

        to = GateApproachDestinationSector;
        t = GateApproachProgress;
        if (to != null)
            return true;

        to = FieldDestinationSector;
        t = FieldTravelProgress;
        return to != null;
    }
}
