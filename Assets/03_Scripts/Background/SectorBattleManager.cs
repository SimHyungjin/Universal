using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MapNav.Ecs;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

// 모든 섹터의 SectorBattleState를 소유하고 백그라운드 점령(영토) 시뮬레이션을 구동하는 단일 진실 매니저.
//
// 제로섬 모델: 섹터 총 병력(ally+enemy) 합은 고정. 한 진영을 줄이면 반대 진영이 그만큼 늘어난다(영역 물들이기).
//  · 점령 게이지 = ally/(ally+enemy) 비율의 파생(별도 누적/튜닝 없음).
//  · 플레이어 섹터: 실제 NavAgent 사망을 폴링 → 죽은 진영 감소·반대 증가(즉시 비율 반영). 화면은 LiveCap을 비율 분배.
//  · 배경 섹터: 전력 우세 진영이 시간당 조금씩 잠식(제로섬, 전력차 비례·상한).
public sealed class SectorBattleManager : IDisposable
{
    private const float LogInterval = 2f; // 검증 로그 주기(설정 대상 아님).
    private const float VisibleTotalEpsilon = 0.5f;

    // 튜닝값은 SO_SectorBattle_Settings에서 주입(없으면 기본값 폴백).
    private readonly SO_SectorBattle_Settings _settings;
    private int   LiveCapTotal      => _settings != null ? _settings.LiveCapTotal      : 200;
    private float CaptureThreshold  => _settings != null ? _settings.CaptureThreshold  : 0.9f;
    private float MutationPerInfluencePerSec => _settings != null ? _settings.MutationPerInfluencePerSec : 0.3f;
    private float MutationMaxPerSec => _settings != null ? _settings.MutationMaxPerSec : 3f;
    private float MutationImmunityDuration => _settings != null ? _settings.MutationImmunityDuration : 3f;
    private int   MutationBurstThreshold => _settings != null ? _settings.MutationBurstThreshold : 5;
    private float SupportPowerRatio => _settings != null ? _settings.SupportPowerRatio : 0.2f;
    private float SupportDistanceFalloff => _settings != null ? _settings.SupportDistanceFalloff : 0.5f;
    private float BattleAttritionPerPowerPerSec => _settings != null ? _settings.BattleAttritionPerPowerPerSec : 0.15f;
    private float BattleAttritionMaxPerSec => _settings != null ? _settings.BattleAttritionMaxPerSec : 4f;

    private readonly Dictionary<Sector, SectorBattleState> _states = new();
    private readonly SectorManager _sectorManager;

    // 침식 시작 시드: 섹터별 시작 점령 진영과 용량(= 그 진영 Total).
    private readonly Func<Sector, NavFaction?> _initialOwner;
    private readonly Func<Sector, int> _capacityOf;
    // 실체화 시 진영별 잡몹 구성(무엇을). 진입 스폰은 이 composition 비율을 사용한다.
    private readonly SO_Sector_AliveComposition _allyComposition;
    private readonly SO_Sector_AliveComposition _enemyComposition;
    private CancellationTokenSource _cts = new();
    private float _logTimer;
    private Sector _polledSector; // 플레이어 섹터 폴링 기준. 섹터가 바뀌면 첫 틱은 동기화만 한다.

    // 링크(연결 컴포넌트) BFS 재사용 버퍼(매 틱 new 방지).
    private readonly HashSet<Sector> _linkVisited = new();
    private readonly Queue<Sector> _linkQueue = new();
    private readonly List<SectorBattleState> _linkComponent = new();
    private readonly HashSet<Sector> _hubVisited = new();

    // 지원 Power 거리 감쇠 BFS 재사용 버퍼(레벨별 탐색, 스왑하므로 non-readonly).
    private readonly HashSet<Sector> _supportVisited = new();
    private List<SectorBattleState> _supportCurrent = new();
    private List<SectorBattleState> _supportNext = new();

    public static SectorBattleManager Instance { get; private set; }
    public IReadOnlyDictionary<Sector, SectorBattleState> States => _states;

    public SectorBattleManager(
        MinimapModel map,
        Sector excludedSector = null,
        SectorManager sectorManager = null,
        SO_SectorBattle_Settings settings = null,
        Func<Sector, NavFaction?> initialOwner = null,
        Func<Sector, int> capacityOf = null,
        SO_Sector_AliveComposition allyComposition = null,
        SO_Sector_AliveComposition enemyComposition = null)
    {
        _sectorManager = sectorManager != null ? sectorManager : SectorManager.Instance;
        _settings = settings;
        _initialOwner = initialOwner;
        _capacityOf = capacityOf;
        _allyComposition = allyComposition;
        _enemyComposition = enemyComposition;
        Instance = this;
        Initialize(map, excludedSector);
        RunAsync(_cts.Token).Forget();
    }

    public bool TryGetState(Sector sector, out SectorBattleState state)
    {
        if (sector == null) { state = null; return false; }
        return _states.TryGetValue(sector, out state);
    }

    // 그 섹터에 자기와 다른 진영의 잡몹 병력이 있는지(장수의 매크로 이동 판단용 — 적대 섹터면 이동 보류).
    public bool HasHostile(Sector sector, NavFaction faction)
    {
        if (!TryGetState(sector, out SectorBattleState state)) return false;
        NavFaction opposite = faction == NavFaction.Ally ? NavFaction.Enemy : NavFaction.Ally;
        return HasVisibleTotal(state, opposite);
    }

    // 진입 핸드오프: 화면 표시 총수(LiveCap)를 진영 비율대로 분배해 스폰 엔트리를 만든다. Total은 건드리지 않는다.
    public NavAgentSpawnEntry[] BuildEntrySpawns(Sector sector)
    {
        if (!TryGetState(sector, out SectorBattleState state))
            return null;

        ResolveLiveTargets(state, out int allyTarget, out int enemyTarget);
        var result = new List<NavAgentSpawnEntry>();
        AppendFactionSpawns(result, sector, NavFaction.Ally, allyTarget);
        AppendFactionSpawns(result, sector, NavFaction.Enemy, enemyTarget);
        return result.ToArray();
    }

    public void Dispose()
    {
        if (Instance == this)
            Instance = null;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _states.Clear();
    }

    // ── 초기화 ───────────────────────────────────────────────────────────────────
    private void Initialize(MinimapModel map, Sector excludedSector)
    {
        if (map?.Nodes == null) return;

        for (int i = 0; i < map.Nodes.Count; i++)
        {
            Sector sector = map.Nodes[i]?.Sector;
            if (sector == null || _states.ContainsKey(sector)) continue;

            var state = new SectorBattleState(sector);
            if (sector != excludedSector)
                SeedInitialTotal(state, sector);
            InitOwnership(state);
            _states.Add(sector, state);
        }
    }

    // 침식 시드가 주입돼 있으면 점령 진영×용량으로 시드한다.
    private void SeedInitialTotal(SectorBattleState state, Sector sector)
    {
        if (_initialOwner != null)
        {
            NavFaction? owner = _initialOwner(sector);
            int capacity = _capacityOf != null ? _capacityOf(sector) : 0;
            if (owner.HasValue && capacity > 0)
                state.AddTotal(owner.Value, capacity);
            return;
        }

        SeedTotal(state, ResolveConfiguredSpawns(sector));
    }

    // 유닛 수 기반: 진영별 시작 유닛 수를 그대로 Total로 시드한다(SectorPower 가중 없음).
    private static void SeedTotal(SectorBattleState state, NavAgentSpawnEntry[] spawns)
    {
        if (spawns == null) return;

        for (int i = 0; i < spawns.Length; i++)
        {
            NavAgentSpawnEntry entry = spawns[i];
            if (entry.Data == null || entry.Count <= 0) continue;
            state.AddTotal(entry.Faction, entry.Count);
        }
    }

    private static void InitOwnership(SectorBattleState state)
        => state.OwnerFaction = state.GaugeNormalized >= 0.5f ? NavFaction.Ally : NavFaction.Enemy;

    // 초기 잡몹 구성 조회(동적 진입 resolver는 거치지 않음).
    private NavAgentSpawnEntry[] ResolveConfiguredSpawns(Sector sector)
        => _sectorManager != null
            ? _sectorManager.GetConfiguredMobSpawns(sector)
            : null;

    // 화면 표시 목표 = LiveCap을 진영 비율로 분배(각 진영 Total을 넘지 않음).
    private void ResolveLiveTargets(SectorBattleState state, out int allyTarget, out int enemyTarget)
    {
        float total = state.TotalSum;
        if (total <= 0f) { allyTarget = 0; enemyTarget = 0; return; }

        allyTarget  = Mathf.Min(Mathf.RoundToInt(LiveCapTotal * state.AllyTotal  / total), Mathf.FloorToInt(state.AllyTotal));
        enemyTarget = Mathf.Min(Mathf.RoundToInt(LiveCapTotal * state.EnemyTotal / total), Mathf.FloorToInt(state.EnemyTotal));
    }

    // ── 틱 ───────────────────────────────────────────────────────────────────────
    public void Tick(float dt)
    {
        if (dt <= 0f) return;

        AccumulateElitePower();
        RecomputeLinks();

        Sector playerSector = _sectorManager != null ? _sectorManager.CurrentSector : null;

        foreach (KeyValuePair<Sector, SectorBattleState> kv in _states)
        {
            SectorBattleState state = kv.Value;
            if (state.Sector == playerSector)
                TickPlayerSector(state, dt);
            else
                TickSector(state, dt);
        }
    }

    // 매 틱 섹터별 엘리트 전력을 다시 합산한다(엘리트는 이동/생사하므로 매번 갱신). 엘리트=전력 가산 가속기.
    private void AccumulateElitePower()
    {
        foreach (KeyValuePair<Sector, SectorBattleState> kv in _states)
        {
            kv.Value.AllyElitePower = 0f;
            kv.Value.EnemyElitePower = 0f;
        }

        IReadOnlyList<Elite_State> elites = Elite_Manager.Instance != null ? Elite_Manager.Instance.Elites : null;
        if (elites == null) return;

        for (int i = 0; i < elites.Count; i++)
        {
            Elite_State e = elites[i];
            if (e == null || !e.IsAlive || e.CurrentSector == null) continue;
            if (!_states.TryGetValue(e.CurrentSector, out SectorBattleState s)) continue;

            float baseElitePower = ResolveBaseElitePower(e);
            if (e.Faction == NavFaction.Ally)
                s.AllyElitePower += baseElitePower;
            else
                s.EnemyElitePower += baseElitePower;
        }
    }

    // 배경 섹터: 전력 우세 진영이 열세 진영을 시간당 조금씩 잠식한다(제로섬, 전력차 비례·상한).
    private float ResolveBaseElitePower(Elite_State elite)
    {
        SO_Character_Data character = elite != null && elite.Data != null ? elite.Data.Character : null;
        return SectorPowerFormula.Calculate(character);
    }

    // 배경 섹터: 링크 압력 변이(양방향) + 양측 공존 시 싸움 시뮬.
    private void TickSector(SectorBattleState state, float dt)
    {
        TickMutation(state, dt, false);
        TickBattle(state, dt);
        UpdateOwnership(state);
    }

    // ── 링크(연결 컴포넌트) ────────────────────────────────────────────────────────
    // 매 틱: 점령 상태 판정 → 같은 진영 완전점령 섹터를 게이트로 묶어 링크 영향력(= 컴포넌트 크기)을 기록한다.
    // 경합 섹터는 링크에서 제외되어 단절점이 된다([[project_defender_dispersal]]의 절단점 = 연결부).
    private void RecomputeLinks()
    {
        int nextLinkId = 1;
        foreach (KeyValuePair<Sector, SectorBattleState> kv in _states)
        {
            SectorBattleState s = kv.Value;
            s.LinkInfluence = 0;
            s.LinkId = 0;
            s.IsLinkHub = false;
            s.Control = ResolveControl(s);

            // 100%(상대 진영 0) 완전 점령에 막 도달한 순간 변이 면역을 부여한다(안정화 유예).
            bool full = s.Control != SectorControl.Contested && (s.AllyTotal <= 0f || s.EnemyTotal <= 0f);
            if (full && !s.WasFullyControlled) s.MutationImmunityTimer = MutationImmunityDuration;
            s.WasFullyControlled = full;
        }

        _linkVisited.Clear();
        foreach (KeyValuePair<Sector, SectorBattleState> kv in _states)
        {
            SectorBattleState start = kv.Value;
            if (start.Control == SectorControl.Contested || _linkVisited.Contains(start.Sector)) continue;

            SectorControl control = start.Control;
            _linkComponent.Clear();
            _linkQueue.Clear();
            _linkVisited.Add(start.Sector);
            _linkQueue.Enqueue(start.Sector);

            while (_linkQueue.Count > 0)
            {
                Sector cur = _linkQueue.Dequeue();
                if (!_states.TryGetValue(cur, out SectorBattleState cs)) continue;
                _linkComponent.Add(cs);

                SectorGate[] gates = cur.Gates;
                if (gates == null) continue;
                for (int i = 0; i < gates.Length; i++)
                {
                    Sector nb = gates[i] != null && gates[i].ConnectedGate != null ? gates[i].ConnectedGate.Sector : null;
                    if (nb == null || _linkVisited.Contains(nb)) continue;
                    if (!_states.TryGetValue(nb, out SectorBattleState ns) || ns.Control != control) continue;
                    _linkVisited.Add(nb);
                    _linkQueue.Enqueue(nb);
                }
            }

            int influence = _linkComponent.Count;
            for (int i = 0; i < _linkComponent.Count; i++)
            {
                _linkComponent[i].LinkInfluence = influence;
                _linkComponent[i].LinkId = nextLinkId;
            }

            SectorBattleState hub = ResolveLinkHub(_linkComponent, control);
            if (hub != null)
                hub.IsLinkHub = true;
            nextLinkId++;
        }
    }

    // 후보 섹터를 제거했을 때 남은 링크 조각들의 제곱합이 가장 작은 곳을 허브로 고른다.
    // 즉, 링크를 여러 개의 고른 크기 조각으로 가장 잘 찢는 절단점이 우선된다.
    private SectorBattleState ResolveLinkHub(List<SectorBattleState> component, SectorControl control)
    {
        if (component == null || component.Count == 0) return null;
        if (component.Count == 1) return component[0];

        SectorBattleState best = null;
        int bestSplitScore = int.MinValue;
        int bestDegree = int.MinValue;

        for (int i = 0; i < component.Count; i++)
        {
            SectorBattleState candidate = component[i];
            int sumSquares = SumRemainingComponentSquares(component, control, candidate.Sector);
            int splitScore = component.Count * component.Count - sumSquares;
            int degree = CountLinkNeighbors(candidate, control);

            if (splitScore > bestSplitScore || (splitScore == bestSplitScore && degree > bestDegree))
            {
                best = candidate;
                bestSplitScore = splitScore;
                bestDegree = degree;
            }
        }

        return best;
    }

    private int SumRemainingComponentSquares(List<SectorBattleState> component, SectorControl control, Sector removed)
    {
        _hubVisited.Clear();
        _hubVisited.Add(removed);
        int sumSquares = 0;

        for (int i = 0; i < component.Count; i++)
        {
            Sector start = component[i].Sector;
            if (start == null || _hubVisited.Contains(start)) continue;

            int size = 0;
            _linkQueue.Clear();
            _linkQueue.Enqueue(start);
            _hubVisited.Add(start);

            while (_linkQueue.Count > 0)
            {
                Sector cur = _linkQueue.Dequeue();
                size++;
                SectorGate[] gates = cur.Gates;
                if (gates == null) continue;

                for (int g = 0; g < gates.Length; g++)
                {
                    Sector nb = gates[g] != null && gates[g].ConnectedGate != null
                        ? gates[g].ConnectedGate.Sector
                        : null;
                    if (nb == null || _hubVisited.Contains(nb)) continue;
                    if (!_states.TryGetValue(nb, out SectorBattleState ns) || ns.Control != control) continue;
                    _hubVisited.Add(nb);
                    _linkQueue.Enqueue(nb);
                }
            }

            sumSquares += size * size;
        }

        return sumSquares;
    }

    private int CountLinkNeighbors(SectorBattleState state, SectorControl control)
    {
        SectorGate[] gates = state?.Sector != null ? state.Sector.Gates : null;
        if (gates == null) return 0;

        int count = 0;
        for (int i = 0; i < gates.Length; i++)
        {
            Sector nb = gates[i] != null && gates[i].ConnectedGate != null ? gates[i].ConnectedGate.Sector : null;
            if (nb != null && _states.TryGetValue(nb, out SectorBattleState ns) && ns.Control == control)
                count++;
        }
        return count;
    }

    private SectorControl ResolveControl(SectorBattleState s)
    {
        if (s.TotalSum <= 0f) return SectorControl.Contested;
        float n = s.GaugeNormalized;
        if (n >= CaptureThreshold) return SectorControl.Ally;
        if (n <= 1f - CaptureThreshold) return SectorControl.Enemy;
        return SectorControl.Contested;
    }

    // ── 변이 (링크 영향력, 양방향 대칭) ────────────────────────────────────────────
    // 들어오는 적/아군 링크 영향력 합을 자기 링크 영향력으로 방어하고, 우세 쪽으로 유닛을 반대 진영으로 전환한다.
    // 배경=Total 제로섬 전환, 플레이어=실체 토글(MutateAgents). 죽임이 아니라 진영 전환(합 보존).
    private void TickMutation(SectorBattleState state, float dt, bool playerSector)
    {
        if (state.TotalSum <= 0f) { state.MutationAccum = 0f; return; }

        // 100% 점령 직후 안정화 유예 — 면역 동안에는 변이를 받지 않는다.
        if (state.MutationImmunityTimer > 0f)
        {
            state.MutationImmunityTimer -= dt;
            state.MutationAccum = 0f;
            return;
        }

        float net = ResolveMutationNet(state); // +면 아군→적, -면 적→아군
        if (Mathf.Abs(net) < 0.0001f) { state.MutationAccum = 0f; return; }

        // 연속이 아니라 누적했다가 임계(N마리)에 도달하면 한 번에 배출한다.
        // 배출 사이에 변이가 멈춘 틈이 생겨 플레이어가 100%를 찍을 여지가 만들어진다.
        state.MutationAccum += Mathf.Clamp(net * MutationPerInfluencePerSec, -MutationMaxPerSec, MutationMaxPerSec) * dt;

        int threshold = Mathf.Max(1, MutationBurstThreshold);
        if (Mathf.Abs(state.MutationAccum) < threshold) return;

        int burst = (int)state.MutationAccum; // 부호 포함, |누적|≥임계이므로 |burst|≥임계
        state.MutationAccum -= burst;

        if (burst > 0) ApplyMutation(state, playerSector, NavFaction.Ally, burst);    // 아군→적
        else           ApplyMutation(state, playerSector, NavFaction.Enemy, -burst);  // 적→아군
    }

    // from 진영 count마리를 반대로 전환. 플레이어 섹터=실체 토글, 배경=Total 제로섬.
    private void ApplyMutation(SectorBattleState state, bool playerSector, NavFaction from, int count)
    {
        if (count <= 0) return;
        if (playerSector)
            MutateAgents(from, count);
        else
            ShiftZeroSum(state, from == NavFaction.Ally ? -count : count);
    }

    // 이 섹터가 받는 순 변이 압력. +면 아군이 적으로, -면 적이 아군으로.
    private float ResolveMutationNet(SectorBattleState state)
    {
        SectorGate[] gates = state.Sector != null ? state.Sector.Gates : null;
        if (gates == null) return 0f;

        int enemyPressure = 0, allyPressure = 0;
        for (int i = 0; i < gates.Length; i++)
        {
            Sector nb = gates[i] != null && gates[i].ConnectedGate != null ? gates[i].ConnectedGate.Sector : null;
            if (nb == null || !_states.TryGetValue(nb, out SectorBattleState ns)) continue;
            if (ns.Control == SectorControl.Ally) allyPressure += ns.LinkInfluence;
            else if (ns.Control == SectorControl.Enemy) enemyPressure += ns.LinkInfluence;
        }

        // 점령지는 "상대 진영" 압력만 받고 자기 링크 영향력으로 방어한다(같은 진영 인접 압력은 무의미).
        // 경합지는 양측이 줄다리기. +면 아군→적, -면 적→아군.
        if (state.Control == SectorControl.Ally)  return enemyPressure - state.LinkInfluence;
        if (state.Control == SectorControl.Enemy) return state.LinkInfluence - allyPressure;
        return enemyPressure - allyPressure;
    }

    // ── 싸움 시뮬 (배경 섹터) ────────────────────────────────────────────────────
    // 양측 전투 주체(현지 병력 또는 엘리트)가 있으면 Power 차이만큼 제로섬 전환.
    // 순수 0% 적 섹터라도 아군 엘리트가 들어오면 AllyTotal을 밀어 올려 전선을 만들 수 있다.
    private void TickBattle(SectorBattleState state, float dt)
    {
        bool allyPresent = state.AllyTotal > 0f || state.AllyElitePower > 0f;
        bool enemyPresent = state.EnemyTotal > 0f || state.EnemyElitePower > 0f;
        if (!allyPresent || !enemyPresent || state.TotalSum <= 0f) return;

        float allyPower  = state.AllyPower  + ResolveSupportPower(state, NavFaction.Ally);
        float enemyPower = state.EnemyPower + ResolveSupportPower(state, NavFaction.Enemy);
        float diff = allyPower - enemyPower; // +면 아군 우세
        if (Mathf.Abs(diff) < 0.0001f) return;

        float amount = Mathf.Min(Mathf.Abs(diff) * BattleAttritionPerPowerPerSec, BattleAttritionMaxPerSec) * dt;
        // diff>0(아군 우세) → 적이 아군으로 = AllyTotal 증가.
        ShiftZeroSum(state, diff > 0 ? amount : -amount);
    }

    // 같은 진영 링크 점령지가 거리 감쇠로 전선을 지원한다: 1칸(인접)=1배, 2칸=falloff, 3칸=falloff² …
    // 링크 따라 BFS(레벨별)로 같은 진영 점령지만 거쳐 가며 거리를 잰다. 큰 링크라도 전선에서 멀면 약하게 기여.
    private float ResolveSupportPower(SectorBattleState state, NavFaction faction)
    {
        if (state.Sector == null) return 0f;
        SectorControl want = faction == NavFaction.Ally ? SectorControl.Ally : SectorControl.Enemy;

        _supportVisited.Clear();
        _supportCurrent.Clear();
        _supportNext.Clear();
        _supportVisited.Add(state.Sector);
        CollectSupportNeighbors(state.Sector, want, _supportCurrent); // 거리 1 진입점

        float support = 0f;
        float factor = 1f;                 // falloff^(거리-1)
        const float minFactor = 0.01f;     // 기여가 무시할 수준이면 조기 종료
        float falloff = Mathf.Clamp01(SupportDistanceFalloff);

        while (_supportCurrent.Count > 0 && factor >= minFactor)
        {
            for (int i = 0; i < _supportCurrent.Count; i++)
            {
                SectorBattleState s = _supportCurrent[i];
                support += (faction == NavFaction.Ally ? s.AllyPower : s.EnemyPower) * factor;
                CollectSupportNeighbors(s.Sector, want, _supportNext);
            }

            (_supportCurrent, _supportNext) = (_supportNext, _supportCurrent);
            _supportNext.Clear();
            factor *= falloff;
        }

        return support * SupportPowerRatio;
    }

    // sector의 게이트 이웃 중 want 진영 점령지를 (아직 방문 안 한 것만) outList에 모으고 방문 표시한다.
    private void CollectSupportNeighbors(Sector sector, SectorControl want, List<SectorBattleState> outList)
    {
        SectorGate[] gates = sector != null ? sector.Gates : null;
        if (gates == null) return;

        for (int i = 0; i < gates.Length; i++)
        {
            Sector nb = gates[i] != null && gates[i].ConnectedGate != null ? gates[i].ConnectedGate.Sector : null;
            if (nb == null || _supportVisited.Contains(nb)) continue;
            if (!_states.TryGetValue(nb, out SectorBattleState ns) || ns.Control != want) continue;
            _supportVisited.Add(nb);
            outList.Add(ns);
        }
    }

    // 제로섬 이동(합 보존). delta>0이면 아군쪽, <0이면 적쪽. 변이·싸움 시뮬이 공용으로 쓴다.
    private static void ShiftZeroSum(SectorBattleState state, float delta)
    {
        float sum = state.TotalSum;
        state.AllyTotal = Mathf.Clamp(state.AllyTotal + delta, 0f, sum);
        state.EnemyTotal = sum - state.AllyTotal;
    }

    // 플레이어 섹터: 게이지=화면 실체 비율(따로 놀지 않음). 부활(적→아군, NavDeathSystem)이 점령 동력이고,
    // 인접 압력 변이(아군↔적)가 균형/반격이다. 죽음=소멸이 아니라 진영 toggle이므로 사망 폴링은 없다.
    private void TickPlayerSector(SectorBattleState state, float dt)
    {
        CountLiveAgents(out int curAlly, out int curEnemy, out int total);

        bool transitioning = _sectorManager != null && _sectorManager.IsTransitioning;
        if (transitioning || _polledSector != state.Sector)
        {
            _polledSector = transitioning ? null : state.Sector;
            state.MutationAccum = 0f;
            UpdateOwnership(state);
            return;
        }

        // ① 링크 압력만큼 화면 실체의 진영을 toggle(변이)한다 — 적의 반격/균형(배경과 공용).
        TickMutation(state, dt, true);

        // ② 게이지 = 화면 실체 비율. 총 병력량(TotalSum)은 보존하고 ally/enemy 비율만 화면에서 가져온다.
        //    부활(적→아군)·변이(양방향)가 화면을 바꾸면 게이지가 그대로 따라온다 — 사망 폴링 불필요.
        int live = curAlly + curEnemy;
        if (live > 0)
        {
            float sum = state.TotalSum;
            state.AllyTotal  = sum * curAlly / live;
            state.EnemyTotal = sum - state.AllyTotal;
        }

        // ③ 총수가 LiveCap에 못 미치면(전투 외 소실 등) 게이지 비율대로 보충. Dying 포함 total로 over-spawn 방지.
        int deficit = LiveCapTotal - total;
        if (deficit > 0)
        {
            int allyNeed  = Mathf.RoundToInt(deficit * state.GaugeNormalized);
            int enemyNeed = deficit - allyNeed;
            curAlly  += allyNeed  > 0 ? ReplenishTo(state, NavFaction.Ally,  curAlly,  curAlly  + allyNeed)  : 0;
            curEnemy += enemyNeed > 0 ? ReplenishTo(state, NavFaction.Enemy, curEnemy, curEnemy + enemyNeed) : 0;
        }

        UpdateOwnership(state);
    }

    private static bool HasVisibleTotal(SectorBattleState state, NavFaction faction)
        => state != null && state.TotalOf(faction) > VisibleTotalEpsilon;

    // 화면 수가 비율 목표에 못 미치면 부족분을 재스폰한다. 반환=실제 스폰 수.
    private int ReplenishTo(SectorBattleState state, NavFaction faction, int cur, int target)
    {
        int need = target - cur;
        if (need <= 0) return 0;

        NavAgentSpawnEntry[] spawns = BuildFactionSpawns(state.Sector, faction, need);
        return spawns != null ? CharacterSpawner.SpawnMobs(spawns) : 0;
    }

    private void UpdateOwnership(SectorBattleState state)
    {
        float n = state.GaugeNormalized;
        if (n >= CaptureThreshold) state.OwnerFaction = NavFaction.Ally;
        else if (n <= 1f - CaptureThreshold) state.OwnerFaction = NavFaction.Enemy;
    }

    // NavAgent를 센다. ally/enemy = 살아있는(죽음 연출 중이 아닌) 실체 = 게이지 비율의 분자/분모.
    // total = 죽어가는(곧 부활) 엔티티까지 포함한 전체 수 = LiveCap 대비 보충(Replenish) 기준.
    // 모든 NavAgent는 플레이어 섹터에만 존재한다.
    private static void CountLiveAgents(out int ally, out int enemy, out int total)
    {
        ally = 0; enemy = 0; total = 0;
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        EntityManager em = world.EntityManager;
        EntityQuery query = em.CreateEntityQuery(
            ComponentType.ReadOnly<NavAgentFaction>(),
            ComponentType.ReadOnly<NavAgentDeath>());
        NativeArray<NavAgentFaction> factions = query.ToComponentDataArray<NavAgentFaction>(Allocator.Temp);
        NativeArray<NavAgentDeath> deaths = query.ToComponentDataArray<NavAgentDeath>(Allocator.Temp);

        total = factions.Length;
        for (int i = 0; i < factions.Length; i++)
        {
            if (deaths[i].Dying != 0) continue;
            if (factions[i].Faction == NavFaction.Ally) ally++;
            else enemy++;
        }

        factions.Dispose();
        deaths.Dispose();
        query.Dispose();
    }

    // 변이(D): from 진영의 살아있는 실체 count마리를 반대 진영으로 toggle한다. 반환=실제 변이 수.
    // 죽어가는(Dying) 엔티티는 곧 부활로 진영이 결정되므로 변이 대상에서 제외한다.
    // faction 변경은 다음 프레임 Unit_NavVisualShell.Tick이 감지해 ConvertTo(파티클+머테리얼)로 연출한다.
    private static int MutateAgents(NavFaction from, int count)
    {
        if (count <= 0) return 0;
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return 0;

        EntityManager em = world.EntityManager;
        EntityQuery query = em.CreateEntityQuery(
            ComponentType.ReadWrite<NavAgentFaction>(),
            ComponentType.ReadOnly<NavAgentDeath>());
        NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);

        NavFaction to = from == NavFaction.Ally ? NavFaction.Enemy : NavFaction.Ally;
        int mutated = 0;
        for (int i = 0; i < entities.Length && mutated < count; i++)
        {
            Entity e = entities[i];
            if (em.GetComponentData<NavAgentDeath>(e).Dying != 0) continue;
            NavAgentFaction f = em.GetComponentData<NavAgentFaction>(e);
            if (f.Faction != from) continue;
            f.Faction = to;
            em.SetComponentData(e, f);
            mutated++;
        }

        entities.Dispose();
        query.Dispose();
        return mutated;
    }

    // ── 스폰 엔트리 빌드 ──────────────────────────────────────────────────────────
    // 한 진영의 잡몹을 구성(종류) 비율대로 target마리 분배해 result에 추가한다.
    // 진영별 구성 SO가 주입돼 있으면 그 비율로 분배한다.
    private void AppendFactionSpawns(List<NavAgentSpawnEntry> result, Sector sector, NavFaction faction, int target)
    {
        if (target <= 0) return;

        SO_Sector_AliveComposition composition = faction == NavFaction.Ally ? _allyComposition : _enemyComposition;
        if (composition != null)
        {
            NavAgentSpawnEntry[] expanded = composition.Expand(faction, target);
            if (expanded != null)
                result.AddRange(expanded);
            return;
        }

        NavAgentSpawnEntry[] configured = ResolveConfiguredSpawns(sector);
        if (configured == null || configured.Length == 0) return;

        float total = 0f;
        for (int i = 0; i < configured.Length; i++)
        {
            NavAgentSpawnEntry e = configured[i];
            if (e.Data == null || e.Count <= 0 || e.Faction != faction) continue;
            total += e.Count;
        }
        if (total <= 0f) return;

        for (int i = 0; i < configured.Length; i++)
        {
            NavAgentSpawnEntry e = configured[i];
            if (e.Data == null || e.Count <= 0 || e.Faction != faction) continue;

            int count = Mathf.RoundToInt(target * (e.Count / total));
            if (count > 0)
                result.Add(new NavAgentSpawnEntry(e.Data, count, faction));
        }
    }

    private NavAgentSpawnEntry[] BuildFactionSpawns(Sector sector, NavFaction faction, int target)
    {
        var result = new List<NavAgentSpawnEntry>(2);
        AppendFactionSpawns(result, sector, faction, target);
        return result.Count > 0 ? result.ToArray() : null;
    }

    // ── 루프 / 검증 로그 ──────────────────────────────────────────────────────────
    private async UniTaskVoid RunAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                float dt = Time.deltaTime;
                Tick(dt);
                LogPeriodic(dt);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void LogPeriodic(float dt)
    {
        _logTimer += dt;
        if (_logTimer < LogInterval) return;
        _logTimer = 0f;

        var sb = new System.Text.StringBuilder("[SectorBattle] ");
        int shown = 0;
        foreach (KeyValuePair<Sector, SectorBattleState> kv in _states)
        {
            SectorBattleState s = kv.Value;
            if (s.TotalSum <= 0f) continue;

            // 진단: [점령상태 L링크영향력 net변이압력 aP아군전투력 eP적전투력 게이지%]  (전투력=현지Power+지원Power)
            float net = ResolveMutationNet(s);
            float aP = s.AllyPower + ResolveSupportPower(s, NavFaction.Ally);
            float eP = s.EnemyPower + ResolveSupportPower(s, NavFaction.Enemy);
            sb.Append($"{s.Sector.DisplayName}[{s.Control} L{s.LinkInfluence} net{net:0.0} aP{aP:0} eP{eP:0} {s.GaugeNormalized * 100f:0}%] ");
            if (++shown >= 12) break;
        }

        if (shown > 0)
            Debug.Log(sb.ToString());
    }
}
