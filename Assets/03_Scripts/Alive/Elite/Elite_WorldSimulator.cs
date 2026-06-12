using System;
using System.Collections.Generic;
using MapNav.Core;
using MapNav.Ecs;
using Unity.Mathematics;
using UnityEngine;

public sealed class Elite_WorldSimulator
{
    private const float IdleRoamSpeedScale = 0.12f;
    private const float CombatApproachSpeedScale = 0.5f;
    private const float CombatEngageRange = 2.5f;
    private const float CombatPulseDistance = 1.1f;
    private const float CombatSideJitter = 0.45f;
    private const float DuelCenterRoamScale = 0.45f;
    private const float DuelMinOrbitRadius = 1.3f;
    private const float DuelPulseOrbitRadius = 1.4f;
    private const float DuelOrbitSpeed = 2.2f;
    private const float GateSlotSpacing = 0.72f;
    private const float GateSlotDepthSpacing = 0.42f;
    private const float PostArrivalThinkDelayMin = 0.22f;
    private const float PostArrivalThinkDelayMax = 0.85f;
    private const float IncomingIntentWaitMin = 0.35f;
    private const float IncomingIntentWaitMax = 1.05f;
    private const bool AllowDuelistTargetHold = false;
    private const int HostileSearchHops = 2; // 2칸 내 적대 점유 섹터를 우선 추적.

    private readonly List<Sector> _knownSectors = new();
    private readonly List<Sector> _neighbors = new();
    private readonly List<SectorBattleState> _defenderHubs = new();
    private readonly List<Elite_State> _factionDefenders = new();

    // BFS(2칸 탐색) 작업용 재사용 버퍼.
    private readonly List<Sector> _bfsNeighbors = new();
    private readonly List<Sector> _bfsVisited = new();
    private readonly Queue<BfsNode> _bfsQueue = new();

    public IReadOnlyList<Sector> KnownSectors => _knownSectors;

    private readonly struct BfsNode
    {
        public readonly Sector Sector;
        public readonly int Depth;
        public BfsNode(Sector sector, int depth) { Sector = sector; Depth = depth; }
    }

    // 본진 결전 소집: 활성이면 소집 진영 엘리트가 역할/교전을 무시하고 _rallySector로 직행한다(순차 도착=웨이브).
    private Sector _rallySector;
    private NavFaction _rallyFaction;
    public bool IsRallying => _rallySector != null;
    public NavFaction RallyFaction => _rallyFaction;
    public void SetRally(Sector capital, NavFaction faction) { _rallySector = capital; _rallyFaction = faction; }
    public void ClearRally() => _rallySector = null;
    private bool IsRallied(Elite_State state)
        => _rallySector != null && state != null && state.Faction == _rallyFaction;

    private readonly SO_SectorBattle_Settings _settings;

    public Elite_WorldSimulator(MinimapModel map = null, SO_SectorBattle_Settings settings = null)
    {
        _settings = settings;
        SetMap(map);
    }

    public void SetMap(MinimapModel map)
    {
        _knownSectors.Clear();
        if (map?.Nodes == null)
            return;

        for (int i = 0; i < map.Nodes.Count; i++)
        {
            Sector sector = map.Nodes[i]?.Sector;
            if (sector != null && !_knownSectors.Contains(sector))
                _knownSectors.Add(sector);
        }
    }

    public bool Tick(
        IReadOnlyList<Elite_State> elites,
        Sector playerSector,
        float deltaTime,
        Func<Sector, NavFaction, bool> hasBackgroundHostile = null)
    {
        if (elites == null || deltaTime <= 0f)
            return false;

        bool needsEmbodimentRefresh = false;
        for (int i = 0; i < elites.Count; i++)
        {
            Elite_State state = elites[i];
            if (state == null || !state.IsAlive)
                continue;

            // 실체화 엘리트: 매크로 이동/게이트 트래블은 안 하지만(실제 전투/이동은 Elite_Brain), "이 섹터를
            // 떠날지"는 매크로 역할 로직으로 결정해 PendingExitSector로 신호한다 → Elite_Brain이 게이트까지
            // 걸어가 통과하면 FinalizeGateExit이 매크로로 복귀시킨다(진입 대쉬의 대칭).
            if (state.Embodiment != null)
            {
                TickEmbodiedExitDecision(state, playerSector, elites, deltaTime, hasBackgroundHostile);
                continue;
            }

            Sector before = state.CurrentSector;
            if (state.TickGateApproach(deltaTime))
                continue;

            if (state.TickFieldTravel(deltaTime))
            {
                state.FieldThinkTimer = ResolvePostArrivalThinkDelay(state);
                if (state.CurrentSector == playerSector || before == playerSector)
                    needsEmbodimentRefresh = true;
                // 게이트로 플레이어 섹터에 진입 → 실체화 시 반대편 게이트에서 통과해 대쉬 진입 연출.
                if (state.CurrentSector == playerSector && before != playerSector)
                {
                    state.BeginGateArrival(before, ResolveGateEntryStart(before, state.CurrentSector, state));
                }
                continue;
            }

            if (state.IsApproachingGate || state.IsFieldTraveling)
            {
                // 게이트 통과(FieldTravel) 후 목적지가 플레이어 섹터면, 도착점까지 가길 기다리지 않고 지금 실체화 트리거.
                // RefreshEmbodiments의 TryBeginObservedGateArrival이 남은 백그라운드 이동을 실체화 대쉬로 승격한다.
                // → 미니맵 도착 후에야 실체화되던 순서 어긋남 해소(게이트 통과 시점 = 실체화 시점).
                if (state.IsFieldTraveling && state.FieldDestinationSector == playerSector)
                    needsEmbodimentRefresh = true;
                continue;
            }

            if (state.CurrentSector == playerSector)
                continue;

            // 본진 결전 소집: 소집 대상 엘리트는 역할/교전을 무시하고 본진으로 직행한다(거리차 순차 도착=웨이브).
            // 본진(_rallySector)은 곧 플레이어 섹터이므로 도착하면 위 분기에서 실체화 대상이 된다.
            if (IsRallied(state))
            {
                state.FieldThinkTimer -= deltaTime;
                if (state.FieldThinkTimer <= 0f)
                {
                    state.FieldThinkTimer = GetThinkInterval(state);
                    Sector hop = ChooseFirstHopTowardSector(state.CurrentSector, _rallySector);
                    if (hop != null)
                        BeginTravel(state, hop);
                }
                continue;
            }

            // 같은 섹터에 적대 대상이 있으면 매 틱 섹터 내 교전 위치를 연출하고(미니맵에서 싸우는 듯) 이동 결정은 보류.
            bool engaged = IsEngagedInCurrentSector(state, elites, playerSector, hasBackgroundHostile);
            if (engaged)
            {
                UpdateCombatMotion(state, elites, deltaTime);
                continue;
            }

            // Duelist는 플레이어와 맞붙지 않는다 — 쫓을 적 엘리트가 남아 있는 한, 플레이어가 같은 섹터에
            // 들어오면 교전(think) 대신 플레이어에게서 가장 먼 인접 섹터로 즉시 도망친다.
            if (DuelistShouldFleePlayer(state, playerSector, elites))
            {
                Sector flee = ChooseFleeSector(state, playerSector);
                if (flee != null)
                {
                    BeginTravel(state, flee);
                    continue;
                }
            }

            UpdateIdleRoamMotion(state, deltaTime);

            state.FieldThinkTimer -= deltaTime;
            if (state.FieldThinkTimer > 0f)
                continue;

            state.FieldThinkTimer = GetThinkInterval(state);

            Sector destination = ChooseDestination(state, playerSector, elites);
            if (destination == null)
                continue;

            BeginTravel(state, destination);
        }

        return needsEmbodimentRefresh;
    }

    // 실체화 엘리트(플레이어 섹터)의 이탈 결정만 돌린다. 싸울 적이 있으면 머물고(전투는 Elite_Brain),
    // 없으면 역할 로직으로 다음 섹터를 골라 PendingExitSector로 신호한다(이동 자체는 Elite_Brain이 걸어서).
    private void TickEmbodiedExitDecision(
        Elite_State state,
        Sector playerSector,
        IReadOnlyList<Elite_State> elites,
        float deltaTime,
        Func<Sector, NavFaction, bool> hasBackgroundHostile)
    {
        // 게이트 통과 대쉬 중이면 결정을 보류(대쉬가 끝나며 매크로로 인계된다).
        if (state.IsGateEntryAnimating)
            return;

        // 이미 이탈 경로가 결정됐으면 교전 여부와 무관하게 유지한다.
        // 반대편에서 플레이어가 들어와도 게이트까지 계속 걸어가 스쳐 지나간다.
        if (state.PendingExitSector != null)
            return;

        // 아직 이탈 결정 전: 교전 대상이 있으면 머물며 싸운다.
        if (IsEngagedInCurrentSector(state, elites, playerSector, hasBackgroundHostile))
            return;

        state.FieldThinkTimer -= deltaTime;
        if (state.FieldThinkTimer > 0f)
            return;
        state.FieldThinkTimer = GetThinkInterval(state);

        Sector destination = ChooseDestination(state, playerSector, elites);
        if (destination == null || destination == state.CurrentSector)
            return;

        // 결정 즉시 게이트 통과 대쉬 — 걸어서 게이트까지 가는 구간을 없앤다(진입 대쉬와 대칭).
        // 도보 구간에서 잡몹에 맞아 넉백·사망하던 문제 해소. 대쉬 중에는 Brain/Action을 꺼 무피격.
        SectorGate gate = FindGate(state.CurrentSector, destination);
        Elite_Brain brain = state.Embodiment != null ? state.Embodiment.GetComponent<Elite_Brain>() : null;
        if (gate != null && Elite_Manager.Instance != null)
            Elite_Manager.Instance.BeginGateExitDash(state, brain, gate);
        else
            state.BeginEmbodiedGateExit(destination); // 폴백: 게이트를 못 찾으면 기존 걷기(Elite_Brain.TickGateExit).
    }

    public void SeedKnownSectorElites(Action<Sector> seedSector)
    {
        if (seedSector == null)
            return;

        for (int i = 0; i < _knownSectors.Count; i++)
            seedSector(_knownSectors[i]);
    }

    private Sector ChooseDestination(Elite_State state, Sector playerSector, IReadOnlyList<Elite_State> elites)
    {
        _neighbors.Clear();
        CollectNeighbors(state.CurrentSector, _neighbors);
        if (_neighbors.Count == 0)
            return null;

        // 2칸 내 적대 점유 섹터 우선. 없으면 글로벌 최근접 적대 섹터로("다 물리쳤다면 가장 근처로 이동").
        Sector target = ChooseRoleTarget(state, playerSector, elites);

        // 적대 대상이 전혀 없으면 기존 랜덤 배회 폴백.
        if (target == null)
        {
            if (GetBattleRole(state) == BattleRole.Defender)
                return null;
            if (UnityEngine.Random.value > 0.35f)
                return null;
            return _neighbors[UnityEngine.Random.Range(0, _neighbors.Count)];
        }

        // 대칭 추격(와리가리) 방지: 대상 섹터를 점유한 적이 "적 장수"라면, 낮은 Id 쪽은 멈춰 기다리고
        // 높은 Id 쪽만 쫓아간다. 동일 스펙 두 장수가 동시에 서로에게 다가가 포탈을 무한히 교환하다
        // 못 만나는 문제를 깬다(한쪽이 고정점이 되어 상대가 와서 만남). 플레이어가 대상이면 늘 추격.
        // hold는 양쪽 다 비실체(백그라운드)일 때만. 실체화된 적(플레이어 섹터)은 필드 이동을 안 하므로
        // 그쪽을 향해 hold하면 영원히 못 만난다 → 실체 적은 늘 추격.
        // 와리가리(포탈 핑퐁) 방지: 서로를 노리는 두 장수가 동시에 이동하면 섹터를 맞바꿔 영영 못 만난다.
        // 한쪽만 멈춰 기다리게 한다 — "높은 Id가 멈춰 기다리고, 낮은 Id가 쫓아가 만난다".
        // 쫓는 쪽을 낮은 Id(아군이 먼저 등록돼 낮음)로 두는 게 도달성에 안전하다: 기다리는 쪽이 적 장수가
        // 회피하는 플레이어 섹터에 있어도, 쫓는 쪽이 회피 제약 없는 진영이면 들어가 만날 수 있다.
        // hold는 둘 다 백그라운드일 때만 — 실체화 엘리트는 게이트로 걸어 나가야 하므로 hold 금지(늘 추격).
        Elite_State hostileElite = FindHostileEliteInSector(state, target, elites);
        if (AllowDuelistTargetHold && hostileElite != null && hostileElite.Embodiment == null
            && state.Embodiment == null && state.Id > hostileElite.Id)
            return null; // hold: 낮은 Id가 이 섹터로 와서 만난다.

        if (target == state.CurrentSector)
        {
            state.FieldThinkTimer = ResolveIncomingIntentWaitDelay(state);
            return null;
        }

        return ChooseFirstHopTowardSector(state.CurrentSector, target) ?? ChooseTowardSector(_neighbors, target);
    }

    // 같은 섹터 적대 대상과 교전하는 듯한 섹터 내 위치 연출(미니맵 마커가 이 위치를 그린다).
    // 적 엘리트가 있으면 그쪽으로 다가가 근접 거리에서 미세 진동, 없으면(잡몹뿐) 섹터 중심에서 교전하듯 움직인다.
    // 적 엘리트가 없을 때 섹터 내를 도는 배회 반경(SO 주입, 없으면 기본값).
    private Sector ChooseRoleTarget(Elite_State state, Sector playerSector, IReadOnlyList<Elite_State> elites)
    {
        switch (GetBattleRole(state))
        {
            case BattleRole.Defender:
                return ChooseDefenderHubTarget(state, elites);

            case BattleRole.Duelist:
                Sector eliteTarget = FindHostileEliteTargetSector(
                    state,
                    elites,
                    playerSector,
                    avoidPlayerSector: state.Faction == NavFaction.Enemy);
                if (eliteTarget != null)
                    return eliteTarget;

                if (state.Faction == NavFaction.Enemy)
                    return playerSector;

                return playerSector != null
                    ? playerSector
                    : ChooseNearestHostileTarget(state, playerSector, elites);

            case BattleRole.Vanguard:
            default:
                return ChooseVanguardMainHubTarget(state)
                       ?? ChooseNearestHostileTarget(state, playerSector, elites);
        }
    }

    // 뱅가드는 상대의 가장 큰 링크 허브를 메인 목표로 삼고, 경로상의 섹터를 뚫으며 전진한다.
    private Sector ChooseVanguardMainHubTarget(Elite_State state)
    {
        Sector best = null;
        int bestInfluence = -1;
        int bestHops = int.MaxValue;
        float bestWorldDistance = float.MaxValue;
        int bestLinkId = int.MaxValue;

        for (int i = 0; i < _knownSectors.Count; i++)
        {
            Sector sector = _knownSectors[i];
            if (!TryGetBattleState(sector, out SectorBattleState battle)
                || !IsHostileLinkHub(battle, state.Faction))
                continue;

            int hops = GetHopDistance(state.CurrentSector, sector);
            float worldDistance = (state.WorldPosition - sector.transform.position).sqrMagnitude;
            if (battle.LinkInfluence > bestInfluence
                || (battle.LinkInfluence == bestInfluence && hops < bestHops)
                || (battle.LinkInfluence == bestInfluence && hops == bestHops && worldDistance < bestWorldDistance)
                || (battle.LinkInfluence == bestInfluence && hops == bestHops
                    && Mathf.Approximately(worldDistance, bestWorldDistance) && battle.LinkId < bestLinkId))
            {
                best = sector;
                bestInfluence = battle.LinkInfluence;
                bestHops = hops;
                bestWorldDistance = worldDistance;
                bestLinkId = battle.LinkId;
            }
        }

        return best;
    }

    private Sector ChooseNearestHostileTarget(
        Elite_State state, Sector playerSector, IReadOnlyList<Elite_State> elites)
    {
        Sector target = FindNearestHostileSectorWithinHops(state, playerSector, elites, HostileSearchHops);
        if (target == null)
            target = FindGlobalNearestHostileSector(state, playerSector, elites);
        return target;
    }

    private float CombatRoamRadius => _settings != null ? _settings.CombatRoamRadius : 9f;

    private void UpdateIdleRoamMotion(Elite_State state, float dt)
    {
        Vector3 center = state.CurrentSector != null ? state.CurrentSector.transform.position : state.WorldPosition;
        Vector3 target = GetRoamTarget(state, center, CombatRoamRadius * 0.7f, 0.28f, 0.65f);
        MoveTowardTarget(state, target, GetFieldMoveSpeed(state) * IdleRoamSpeedScale * dt, 0f);
        ConstrainStateToSectorNav(state);
    }

    private void UpdateCombatMotion(Elite_State state, IReadOnlyList<Elite_State> elites, float dt)
    {
        Elite_State foe = FindHostileEliteInSector(state, state.CurrentSector, elites);
        Vector3 center = state.CurrentSector != null ? state.CurrentSector.transform.position : state.WorldPosition;

        if (foe != null)
        {
            UpdateDuelMotion(state, foe, center, dt);
            return;
        }

        // 적 엘리트가 있으면 그쪽으로 맞붙고, 없으면(잡몹뿐) 섹터 안을 천천히 배회한다(중심 고정 탈피, 엘리트별 위상).
        Vector3 anchor = GetRoamTarget(state, center, CombatRoamRadius, 0.38f, 0.8f);

        Vector3 to = anchor - state.WorldPosition;
        to.y = 0f;
        float dist = to.magnitude;
        Vector3 baseDir = to.sqrMagnitude > 0.0001f ? to.normalized : state.Forward;
        float speed = GetFieldMoveSpeed(state) * CombatApproachSpeedScale;

        if (dist > CombatEngageRange)
        {
            // 교전 지점/적으로 이동(필드 이동보다 느리게). 배회 목표를 쫓으며 섹터 안을 돌게 된다.
            state.WorldPosition += baseDir * Mathf.Min(dist - CombatEngageRange, speed * dt);
            state.Forward = baseDir;
        }
        else
        {
            // 근접 교전: 적/지점을 향한 채 좌우·전후로 미세 진동(맞붙은 공방 느낌).
            Vector3 side = new Vector3(-baseDir.z, 0f, baseDir.x);
            float phase = Time.time * 5.5f + state.Id * 1.7f;
            float pulse = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Time.time * 3.2f + state.Id * 1.37f)), 3f);
            Vector3 jitter = side * (Mathf.Sin(phase) * CombatSideJitter)
                             + baseDir * (Mathf.Sin(phase * 2.1f) * 0.18f + pulse * CombatPulseDistance);
            state.WorldPosition = anchor - baseDir * CombatEngageRange + jitter;
            state.Forward = baseDir;
        }

        ConstrainStateToSectorNav(state);
    }

    private void UpdateDuelMotion(Elite_State state, Elite_State foe, Vector3 sectorCenter, float dt)
    {
        int minId = Mathf.Min(state.Id, foe.Id);
        int maxId = Mathf.Max(state.Id, foe.Id);

        Vector3 pairCenter = (state.WorldPosition + foe.WorldPosition) * 0.5f;
        pairCenter.y = sectorCenter.y;

        Vector3 centerTarget = GetDuelCenterTarget(sectorCenter, minId, maxId);
        pairCenter = Vector3.Lerp(pairCenter, centerTarget, 0.35f);

        float phase = Time.time * DuelOrbitSpeed + minId * 1.17f + maxId * 0.43f;
        float pulse = Mathf.SmoothStep(0f, 1f, Mathf.Sin(Time.time * 3.4f + minId * 0.71f + maxId * 0.29f) * 0.5f + 0.5f);
        float orbitRadius = DuelMinOrbitRadius + pulse * DuelPulseOrbitRadius;

        Vector3 orbit = new Vector3(Mathf.Cos(phase), 0f, Mathf.Sin(phase));
        float sign = state.Id < foe.Id ? 1f : -1f;
        Vector3 tangent = new Vector3(-orbit.z, 0f, orbit.x) * sign;
        float wobble = Mathf.Sin(Time.time * 7f + state.Id * 1.31f) * CombatSideJitter;

        Vector3 target = pairCenter + orbit * (sign * orbitRadius) + tangent * wobble;
        float speed = GetFieldMoveSpeed(state) * CombatApproachSpeedScale * 1.15f;
        MoveTowardTarget(state, target, speed * dt, 0f);

        Vector3 face = foe.WorldPosition - state.WorldPosition;
        face.y = 0f;
        if (face.sqrMagnitude > 0.0001f)
            state.Forward = face.normalized;

        ConstrainStateToSectorNav(state);
    }

    private Vector3 GetDuelCenterTarget(Vector3 sectorCenter, int minId, int maxId)
    {
        float radius = CombatRoamRadius * DuelCenterRoamScale;
        float phase = Time.time * 0.24f + minId * 1.43f + maxId * 0.67f;
        float secondary = Time.time * 0.19f + minId * 0.53f + maxId * 1.11f;
        return sectorCenter + new Vector3(
            Mathf.Cos(phase) * radius,
            0f,
            Mathf.Sin(secondary) * radius * 0.75f);
    }

    // Smooth, offset per-elite orbit target used for minimap/background motion.
    private static Vector3 GetRoamTarget(Elite_State state, Vector3 center, float radius, float angularSpeed, float yScale)
    {
        float phase = Time.time * angularSpeed + state.Id * 2.31f;
        float secondary = Time.time * angularSpeed * 0.73f + state.Id * 0.91f;
        float x = Mathf.Cos(phase) * radius;
        float z = Mathf.Sin(secondary) * radius * yScale;
        return center + new Vector3(x, 0f, z);
    }

    private static void MoveTowardTarget(Elite_State state, Vector3 target, float maxDistance, float stopDistance)
    {
        Vector3 to = target - state.WorldPosition;
        to.y = 0f;
        float dist = to.magnitude;
        if (dist <= Mathf.Max(0f, stopDistance))
            return;

        Vector3 dir = dist > 0.0001f ? to / dist : state.Forward;
        state.WorldPosition += dir * Mathf.Min(dist - stopDistance, Mathf.Max(0f, maxDistance));
        state.Forward = dir;
    }

    private static void ConstrainStateToSectorNav(Elite_State state)
    {
        if (state == null)
            return;
        if (TryResolveNavSafePosition(state.CurrentSector, state.WorldPosition, GetAgentRadius(state), out Vector3 safe))
            state.WorldPosition = safe;
    }

    private static bool TryResolveNavSafePosition(Sector sector, Vector3 position, float agentRadius, out Vector3 safe)
    {
        safe = position;
        MapNavigationAuthoring map = sector != null ? sector.NavAuthoring : null;
        if (map == null || !map.NavBlobData.IsCreated)
            return false;

        var ctx = new NavContext(
            map.NavBlobData,
            map.transform.localToWorldMatrix,
            map.transform.worldToLocalMatrix);

        const float boundaryTolerance = 0.05f;
        float3 resolved = position;
        if (!NavQuery.TryClassify(in ctx, resolved, boundaryTolerance, out _)
            || !NavQuery.IsClearOfObstaclePadding(in ctx, resolved, agentRadius))
        {
            if (NavQuery.TryProjectToNearestSpace(in ctx, resolved, out float3 projected, out _))
                resolved = projected;

            if (NavQuery.TryProjectOutOfObstacle(in ctx, resolved, agentRadius, out float3 clear))
                resolved = clear;
        }

        if (NavAgentCore.TrySnapHeight(in ctx, resolved, boundaryTolerance, 0f, out float3 snapped))
            resolved = snapped;

        safe = resolved;
        return true;
    }

    // Nearest living hostile elite in this sector. Returns null when only background mobs are present.
    private static Elite_State FindHostileEliteInSector(Elite_State state, Sector sector, IReadOnlyList<Elite_State> elites)
    {
        Elite_State best = null;
        float bestDist = float.MaxValue;
        Vector3 from = state.WorldPosition;

        for (int i = 0; i < elites.Count; i++)
        {
            Elite_State e = elites[i];
            if (e == null || e == state || !e.IsAlive) continue;
            if (e.Faction == state.Faction || e.CurrentSector != sector) continue;

            float d = (e.WorldPosition - from).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = e;
            }
        }

        return best;
    }

    private static Sector FindHostileEliteTargetSector(
        Elite_State state,
        IReadOnlyList<Elite_State> elites,
        Sector playerSector,
        bool avoidPlayerSector)
    {
        Sector best = null;
        float bestDist = float.MaxValue;
        Vector3 from = state.WorldPosition;

        for (int i = 0; i < elites.Count; i++)
        {
            Elite_State e = elites[i];
            if (e == null || e == state || !e.IsAlive) continue;
            if (e.Faction == state.Faction) continue;

            Sector committed = ResolveCommittedSector(e);
            if (committed == null) continue;
            if (avoidPlayerSector && committed == playerSector) continue;

            float d = (committed.transform.position - from).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = committed;
            }
        }

        return best;
    }

    private static Sector ResolveCommittedSector(Elite_State state)
    {
        if (state == null)
            return null;
        if (state.PendingExitSector != null)
            return state.PendingExitSector;
        if (state.GateApproachDestinationSector != null)
            return state.GateApproachDestinationSector;
        if (state.FieldDestinationSector != null)
            return state.FieldDestinationSector;
        return state.CurrentSector;
    }

    private static float ResolvePostArrivalThinkDelay(Elite_State state)
        => ResolveShortThinkDelay(state, PostArrivalThinkDelayMin, PostArrivalThinkDelayMax);

    private static float ResolveIncomingIntentWaitDelay(Elite_State state)
        => ResolveShortThinkDelay(state, IncomingIntentWaitMin, IncomingIntentWaitMax);

    private static float ResolveShortThinkDelay(Elite_State state, float min, float max)
    {
        float seed = ((state != null ? state.Id : 0) + 1) * 12.9898f + Time.time * 0.73f;
        float jitter = Mathf.Repeat(Mathf.Sin(seed) * 43758.5453f, 1f);
        return Mathf.Lerp(min, max, jitter);
    }

    private static bool TryGetBattleState(Sector sector, out SectorBattleState battle)
    {
        battle = null;
        return sector != null
               && SectorBattleManager.Instance != null
               && SectorBattleManager.Instance.TryGetState(sector, out battle);
    }

    private static BattleRole GetBattleRole(Elite_State state)
    {
        SO_Character_Data character = state != null && state.Data != null ? state.Data.Character : null;
        return character != null ? character.BattleRole : BattleRole.Vanguard;
    }

    // 현재 섹터에서 교전(섹터 내 전투 연출)에 들어갈지. Duelist는 "엘리트만 전투" — 잡몹/플레이어와는
    // 맞붙지 않고 추격·도망한다. 단, 적 엘리트가 전멸하면 마지막 수단으로 플레이어와 교전한다.
    private bool IsEngagedInCurrentSector(
        Elite_State state,
        IReadOnlyList<Elite_State> elites,
        Sector playerSector,
        Func<Sector, NavFaction, bool> hasBackgroundHostile)
    {
        if (GetBattleRole(state) == BattleRole.Duelist)
        {
            if (FindHostileEliteInSector(state, state.CurrentSector, elites) != null)
                return true;
            // 추격할 엘리트가 더 없으면 도망을 멈추고 플레이어와 교전(최후 수단).
            return !AnyHostileEliteAlive(state, elites)
                   && state.Faction == NavFaction.Enemy
                   && state.CurrentSector == playerSector;
        }

        // 디펜더는 자기 허브에서 교전하되, 플레이어가 들어온 섹터면 허브가 아니어도 떠나지 않고 맞붙는다
        // (플레이어 앞에서 등 돌리고 도망가는 게 불쾌 + 장수 1:1 결투가 게임 깊이). 플레이어가 떠나면 다시 허브로 복귀.
        // 그 외 비허브 전투에는 붙잡히지 않고 자기 진영 허브로 복귀한다.
        if (GetBattleRole(state) == BattleRole.Defender)
        {
            bool atPlayerSector = state.CurrentSector == playerSector;
            return (atPlayerSector || IsOwnLinkHub(state.CurrentSector, state.Faction))
                   && (SectorHasHostile(state.CurrentSector, state.Faction, elites, playerSector)
                       || (hasBackgroundHostile != null && hasBackgroundHostile(state.CurrentSector, state.Faction)));
        }

        return SectorHasHostile(state.CurrentSector, state.Faction, elites, playerSector)
               || (hasBackgroundHostile != null && hasBackgroundHostile(state.CurrentSector, state.Faction));
    }

    // Duelist(적군)가 플레이어와 같은 섹터에 들어왔고, 아직 쫓을 적 엘리트가 남아 있으면 도망한다.
    // (엘리트가 전무하면 spec상 플레이어를 목표로 삼으므로 도망하지 않는다 — IsEngaged 쪽에서 교전.)
    private static bool DuelistShouldFleePlayer(Elite_State state, Sector playerSector, IReadOnlyList<Elite_State> elites)
    {
        if (GetBattleRole(state) != BattleRole.Duelist)
            return false;
        if (state.Faction != NavFaction.Enemy || playerSector == null || state.CurrentSector != playerSector)
            return false;
        return AnyHostileEliteAlive(state, elites);
    }

    private static bool AnyHostileEliteAlive(Elite_State state, IReadOnlyList<Elite_State> elites)
    {
        for (int i = 0; i < elites.Count; i++)
        {
            Elite_State e = elites[i];
            if (e == null || e == state || !e.IsAlive) continue;
            if (e.Faction != state.Faction) return true;
        }
        return false;
    }

    // 플레이어에게서 가장 먼(플레이어 섹터 자신은 제외) 인접 섹터.
    private Sector ChooseFleeSector(Elite_State state, Sector playerSector)
    {
        _neighbors.Clear();
        CollectNeighbors(state.CurrentSector, _neighbors);
        if (_neighbors.Count == 0)
            return null;

        Vector3 playerPos = playerSector != null ? playerSector.transform.position : state.WorldPosition;
        Sector best = null;
        float bestDist = -1f;
        for (int i = 0; i < _neighbors.Count; i++)
        {
            Sector n = _neighbors[i];
            if (n == null || n == playerSector) continue;
            float d = (n.transform.position - playerPos).sqrMagnitude;
            if (d > bestDist)
            {
                bestDist = d;
                best = n;
            }
        }
        return best;
    }

    private Sector FindNearestHostileSectorWithinHops(
        Elite_State state, Sector playerSector, IReadOnlyList<Elite_State> elites, int maxHops)
        => FindNearestSectorWithinHops(state.CurrentSector, state.WorldPosition, maxHops,
            s => SectorHasHostile(s, state.Faction, elites, playerSector));

    // 같은 진영 디펜더를 링크 크기(LinkInfluence)에 비례해 각 허브에 안정적으로 분배한다.
    private Sector ChooseDefenderHubTarget(Elite_State state, IReadOnlyList<Elite_State> elites)
    {
        _defenderHubs.Clear();
        for (int i = 0; i < _knownSectors.Count; i++)
        {
            if (TryGetBattleState(_knownSectors[i], out SectorBattleState battle)
                && IsOwnLinkHub(battle, state.Faction))
                _defenderHubs.Add(battle);
        }

        if (_defenderHubs.Count == 0)
            return null;

        _defenderHubs.Sort((a, b) => a.LinkId.CompareTo(b.LinkId));

        _factionDefenders.Clear();
        for (int i = 0; i < elites.Count; i++)
        {
            Elite_State elite = elites[i];
            if (elite != null && elite.IsAlive && elite.Faction == state.Faction
                && GetBattleRole(elite) == BattleRole.Defender)
                _factionDefenders.Add(elite);
        }

        int totalInfluence = 0;
        for (int i = 0; i < _defenderHubs.Count; i++)
            totalInfluence += Mathf.Max(1, _defenderHubs[i].LinkInfluence);

        // 먼저 링크 크기에 비례한 허브별 필요 인원 수를 확정한다.
        int[] hubQuotas = new int[_defenderHubs.Count];
        int reservedDefenders = 0;
        if (_factionDefenders.Count >= _defenderHubs.Count)
        {
            for (int hubIndex = 0; hubIndex < _defenderHubs.Count; hubIndex++)
                hubQuotas[hubIndex] = 1;
            reservedDefenders = _defenderHubs.Count;
        }

        int proportionalDefenders = _factionDefenders.Count - reservedDefenders;
        for (int defenderIndex = 0; defenderIndex < proportionalDefenders; defenderIndex++)
        {
            int influenceSlot = Mathf.Min(
                totalInfluence - 1,
                Mathf.FloorToInt((defenderIndex + 0.5f) * totalInfluence / proportionalDefenders));

            int accumulated = 0;
            for (int hubIndex = 0; hubIndex < _defenderHubs.Count; hubIndex++)
            {
                accumulated += Mathf.Max(1, _defenderHubs[hubIndex].LinkInfluence);
                if (influenceSlot < accumulated)
                {
                    hubQuotas[hubIndex]++;
                    break;
                }
            }
        }

        // 비례 몫 안에서는 가까운 디펜더부터 배치한다. 지형을 무시한 ID 배정으로 적 영역을
        // 불필요하게 가로지르거나 플레이어 섹터에 갇히는 상황을 줄인다.
        var assigned = new HashSet<Elite_State>();
        while (assigned.Count < _factionDefenders.Count)
        {
            Elite_State bestDefender = null;
            int bestHubIndex = -1;
            int bestHops = int.MaxValue;
            float bestWorldDistance = float.MaxValue;

            for (int defenderIndex = 0; defenderIndex < _factionDefenders.Count; defenderIndex++)
            {
                Elite_State defender = _factionDefenders[defenderIndex];
                if (assigned.Contains(defender))
                    continue;

                for (int hubIndex = 0; hubIndex < _defenderHubs.Count; hubIndex++)
                {
                    if (hubQuotas[hubIndex] <= 0)
                        continue;

                    Sector hub = _defenderHubs[hubIndex].Sector;
                    int hops = GetHopDistance(defender.CurrentSector, hub);
                    float worldDistance = (defender.WorldPosition - hub.transform.position).sqrMagnitude;

                    if (hops < bestHops
                        || (hops == bestHops && worldDistance < bestWorldDistance)
                        || (hops == bestHops && Mathf.Approximately(worldDistance, bestWorldDistance)
                            && (bestDefender == null || defender.Id < bestDefender.Id)))
                    {
                        bestDefender = defender;
                        bestHubIndex = hubIndex;
                        bestHops = hops;
                        bestWorldDistance = worldDistance;
                    }
                }
            }

            if (bestDefender == null || bestHubIndex < 0)
                return null;

            assigned.Add(bestDefender);
            hubQuotas[bestHubIndex]--;
            if (bestDefender == state)
                return _defenderHubs[bestHubIndex].Sector;
        }

        return null;
    }

    private static bool IsOwnLinkHub(Sector sector, NavFaction faction)
        => TryGetBattleState(sector, out SectorBattleState battle) && IsOwnLinkHub(battle, faction);

    private static bool IsOwnLinkHub(SectorBattleState battle, NavFaction faction)
        => battle != null
           && battle.IsLinkHub
           && (faction == NavFaction.Ally
               ? battle.Control == SectorControl.Ally
               : battle.Control == SectorControl.Enemy);

    private static bool IsHostileLinkHub(SectorBattleState battle, NavFaction faction)
        => battle != null
           && battle.IsLinkHub
           && (faction == NavFaction.Ally
               ? battle.Control == SectorControl.Enemy
               : battle.Control == SectorControl.Ally);

    private static int GetHopDistance(Sector origin, Sector target)
    {
        if (origin == null || target == null)
            return int.MaxValue;
        if (origin == target)
            return 0;

        var visited = new HashSet<Sector> { origin };
        var queue = new Queue<BfsNode>();
        var neighbors = new List<Sector>();
        queue.Enqueue(new BfsNode(origin, 0));

        while (queue.Count > 0)
        {
            BfsNode node = queue.Dequeue();
            neighbors.Clear();
            CollectNeighbors(node.Sector, neighbors);
            for (int i = 0; i < neighbors.Count; i++)
            {
                Sector neighbor = neighbors[i];
                if (neighbor == null || !visited.Add(neighbor))
                    continue;
                if (neighbor == target)
                    return node.Depth + 1;
                queue.Enqueue(new BfsNode(neighbor, node.Depth + 1));
            }
        }

        return int.MaxValue;
    }

    // origin에서 게이트 BFS(깊이 maxHops)로 match를 만족하는 (홉, 그다음 월드거리) 최근접 섹터.
    private Sector FindNearestSectorWithinHops(Sector origin, Vector3 from, int maxHops, Func<Sector, bool> match)
    {
        if (origin == null)
            return null;

        _bfsVisited.Clear();
        _bfsQueue.Clear();
        _bfsVisited.Add(origin);
        _bfsQueue.Enqueue(new BfsNode(origin, 0));

        Sector best = null;
        int bestDepth = int.MaxValue;
        float bestDist = float.MaxValue;

        while (_bfsQueue.Count > 0)
        {
            BfsNode node = _bfsQueue.Dequeue();

            if (node.Sector != origin && match(node.Sector))
            {
                float dist = (node.Sector.transform.position - from).sqrMagnitude;
                if (node.Depth < bestDepth || (node.Depth == bestDepth && dist < bestDist))
                {
                    best = node.Sector;
                    bestDepth = node.Depth;
                    bestDist = dist;
                }
            }

            if (node.Depth >= maxHops)
                continue;

            _bfsNeighbors.Clear();
            CollectNeighbors(node.Sector, _bfsNeighbors);
            for (int i = 0; i < _bfsNeighbors.Count; i++)
            {
                Sector n = _bfsNeighbors[i];
                if (n == null || _bfsVisited.Contains(n))
                    continue;
                _bfsVisited.Add(n);
                _bfsQueue.Enqueue(new BfsNode(n, node.Depth + 1));
            }
        }

        return best;
    }

    // 맵 전체에서 자기와 다른 진영의 살아있는 장수(또는 Enemy 입장의 playerSector)가 있는 최근접 섹터.
    private static Sector FindGlobalNearestHostileSector(
        Elite_State state, Sector playerSector, IReadOnlyList<Elite_State> elites)
    {
        Sector best = null;
        float bestDist = float.MaxValue;
        Vector3 from = state.WorldPosition;

        void Consider(Sector s)
        {
            if (s == null || s == state.CurrentSector)
                return;
            float d = (s.transform.position - from).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = s;
            }
        }

        if (state.Faction == NavFaction.Enemy)
            Consider(playerSector);

        for (int i = 0; i < elites.Count; i++)
        {
            Elite_State e = elites[i];
            if (e == null || e == state || !e.IsAlive) continue;
            if (e.Faction == state.Faction) continue;
            Consider(ResolveCommittedSector(e));
        }

        return best;
    }

    // 해당 섹터에 자기와 다른 진영의 적대 대상(살아있는 적 장수, 또는 Enemy 입장의 플레이어)이 있는지.
    private static bool SectorHasHostile(
        Sector sector, NavFaction myFaction, IReadOnlyList<Elite_State> elites, Sector playerSector)
    {
        if (sector == null)
            return false;

        // 플레이어(Ally)는 playerSector에 있다 → Enemy 장수에게만 적대.
        if (myFaction == NavFaction.Enemy && sector == playerSector)
            return true;

        for (int i = 0; i < elites.Count; i++)
        {
            Elite_State e = elites[i];
            if (e == null || !e.IsAlive) continue;
            if (e.Faction == myFaction) continue;
            if (e.CurrentSector == sector) return true;
        }

        return false;
    }

    private static Sector ChooseFirstHopTowardSector(Sector origin, Sector target)
    {
        if (origin == null || target == null || origin == target)
            return null;

        var visited = new HashSet<Sector> { origin };
        var queue = new Queue<Sector>();
        var firstHopBySector = new Dictionary<Sector, Sector>();
        var neighbors = new List<Sector>();
        Vector3 targetPosition = target.transform.position;

        CollectNeighbors(origin, neighbors);
        neighbors.Sort((a, b) =>
        {
            float da = a != null ? (a.transform.position - targetPosition).sqrMagnitude : float.MaxValue;
            float db = b != null ? (b.transform.position - targetPosition).sqrMagnitude : float.MaxValue;
            return da.CompareTo(db);
        });

        for (int i = 0; i < neighbors.Count; i++)
        {
            Sector neighbor = neighbors[i];
            if (neighbor == null || !visited.Add(neighbor))
                continue;

            firstHopBySector[neighbor] = neighbor;
            if (neighbor == target)
                return neighbor;

            queue.Enqueue(neighbor);
        }

        while (queue.Count > 0)
        {
            Sector sector = queue.Dequeue();
            Sector firstHop = firstHopBySector[sector];

            neighbors.Clear();
            CollectNeighbors(sector, neighbors);
            for (int i = 0; i < neighbors.Count; i++)
            {
                Sector neighbor = neighbors[i];
                if (neighbor == null || !visited.Add(neighbor))
                    continue;

                firstHopBySector[neighbor] = firstHop;
                if (neighbor == target)
                    return firstHop;

                queue.Enqueue(neighbor);
            }
        }

        return null;
    }

    private static Sector ChooseTowardSector(IReadOnlyList<Sector> candidates, Sector target)
    {
        Vector3 targetPosition = target.transform.position;
        Sector best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < candidates.Count; i++)
        {
            Sector candidate = candidates[i];
            if (candidate == null)
                continue;

            float distance = (candidate.transform.position - targetPosition).sqrMagnitude;
            if (distance >= bestDistance)
                continue;

            best = candidate;
            bestDistance = distance;
        }

        return best;
    }

    public static void BeginMacroTravel(Elite_State state, Sector destination)
        => BeginTravel(state, destination);

    public static Vector3 ResolveGateExitArrival(Sector from, Sector to)
        => ResolveArrivalPosition(from, to);

    public static Vector3 ResolveGateExitArrival(Sector from, Sector to, Elite_State state)
        => ResolveArrivalPosition(from, to, state);

    public static Vector3 ResolveGateDeparturePosition(Sector from, Sector to, Elite_State state)
        => ResolveDeparturePosition(from, to, state);

    public static void BeginMacroArrivalTravel(Elite_State state, Sector destination)
    {
        if (state == null || destination == null || destination == state.CurrentSector)
            return;

        Vector3 endPosition = ResolveArrivalPosition(state.CurrentSector, destination, state);
        if (TryResolveNavSafePosition(destination, endPosition, GetAgentRadius(state), out Vector3 safeEnd))
            endPosition = safeEnd;

        float speed = GetFieldMoveSpeed(state);
        float travelDistance = Vector3.Distance(state.WorldPosition, endPosition);
        state.BeginFieldTravel(destination, endPosition, travelDistance / speed);
    }

    private static void BeginTravel(Elite_State state, Sector destination)
    {
        Vector3 gatePosition = ResolveDeparturePosition(state.CurrentSector, destination, state);
        if (TryResolveNavSafePosition(state.CurrentSector, gatePosition, GetAgentRadius(state), out Vector3 safeGate))
            gatePosition = safeGate;

        Vector3 endPosition = ResolveArrivalPosition(state.CurrentSector, destination, state);
        if (TryResolveNavSafePosition(destination, endPosition, GetAgentRadius(state), out Vector3 safeEnd))
            endPosition = safeEnd;

        float speed = GetFieldMoveSpeed(state);
        float approachDistance = Vector3.Distance(state.WorldPosition, gatePosition);
        float travelDistance = Vector3.Distance(gatePosition, endPosition);

        if (approachDistance > 0.05f)
            state.BeginGateApproach(destination, gatePosition, endPosition, approachDistance / speed, travelDistance / speed);
        else
            state.BeginFieldTravel(destination, endPosition, travelDistance / speed);
    }

    private static void CollectNeighbors(Sector sector, List<Sector> results)
    {
        if (sector?.Gates == null)
            return;

        for (int i = 0; i < sector.Gates.Length; i++)
        {
            SectorGate gate = sector.Gates[i];
            Sector neighbor = gate != null && gate.ConnectedGate != null
                ? gate.ConnectedGate.Sector
                : null;
            if (neighbor != null && !results.Contains(neighbor))
                results.Add(neighbor);
        }
    }

    private static Vector3 ResolveArrivalPosition(Sector from, Sector to)
        => ResolveArrivalPosition(from, to, null);

    private static Vector3 ResolveArrivalPosition(Sector from, Sector to, Elite_State state)
    {
        SectorGate gate = FindGate(from, to);
        if (gate != null && gate.ConnectedGate != null)
            return ApplyGateSlotOffset(gate.ConnectedGate.SpawnPosition, gate.ConnectedGate, state);

        return to != null ? to.transform.position : Vector3.zero;
    }

    private static Vector3 ResolveDeparturePosition(Sector from, Sector to)
        => ResolveDeparturePosition(from, to, null);

    private static Vector3 ResolveDeparturePosition(Sector from, Sector to, Elite_State state)
    {
        SectorGate gate = FindGate(from, to);
        if (gate != null)
            return ApplyGateSlotOffset(gate.SpawnPosition, gate, state);

        return from != null ? from.transform.position : Vector3.zero;
    }

    // 게이트 대쉬 진입의 출발점 = 반대편(출발 섹터 from) 게이트 지점. 여기서 스폰해 게이트를 통과해 도착 지점까지 대쉬한다.
    public static Vector3 ResolveGateEntryStart(Sector from, Sector to)
    {
        SectorGate gate = FindGate(from, to);
        if (gate != null)
            return gate.SpawnPosition;

        return from != null ? from.transform.position : Vector3.zero;
    }

    public static Vector3 ResolveGateEntryStart(Sector from, Sector to, Elite_State state)
    {
        SectorGate gate = FindGate(from, to);
        if (gate != null)
            return ApplyGateSlotOffset(gate.SpawnPosition, gate, state);

        return from != null ? from.transform.position : Vector3.zero;
    }

    private static Vector3 ApplyGateSlotOffset(Vector3 position, SectorGate gate, Elite_State state)
    {
        if (state == null)
            return position;

        int lane = state.Id % 5;
        int depth = (state.Id / 5) % 3;
        float side = lane - 2f;
        float back = depth - 1f;

        Vector3 right = gate != null ? gate.transform.right : Vector3.right;
        right.y = 0f;
        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.right;
        right.Normalize();

        Vector3 forward = gate != null ? gate.transform.forward : Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        return position + right * (side * GateSlotSpacing) - forward * (back * GateSlotDepthSpacing);
    }

    private static SectorGate FindGate(Sector from, Sector to)
    {
        if (from?.Gates == null || to == null)
            return null;

        for (int i = 0; i < from.Gates.Length; i++)
        {
            SectorGate gate = from.Gates[i];
            if (gate != null && gate.ConnectedGate != null && gate.ConnectedGate.Sector == to)
                return gate;
        }

        return null;
    }

    private static float GetFieldMoveSpeed(Elite_State state)
    {
        SO_Elite_Brain brain = state?.Data != null ? state.Data.Brain : null;
        return brain != null ? Mathf.Max(0.01f, brain.FieldMoveSpeed) : 35f;
    }

    private static float GetAgentRadius(Elite_State state)
    {
        SO_Elite_Brain brain = state?.Data != null ? state.Data.Brain : null;
        return brain != null ? Mathf.Max(0f, brain.AgentRadius) : 0.35f;
    }

    private static float GetThinkInterval(Elite_State state)
    {
        SO_Elite_Brain brain = state?.Data != null ? state.Data.Brain : null;
        return brain != null ? Mathf.Max(0.1f, brain.FieldThinkInterval) : 5f;
    }
}
