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
//  · (A) 통합 점령 압력: 위상(net=링크 영역 크기)과 전투력(aP-eP)을 단일 enemyward로 합성해 점령을 민다.
//  · 배경 섹터(TickConquest): enemyward를 Total 제로섬 전환(ShiftZeroSum)으로 적용.
//  · 플레이어 섹터(TickPlayerConquest): 같은 enemyward를 실체 toggle(MutateAgents)로 적용. 단 아군 우세(≤0)면
//    적 주입 정지 — 아군화는 오직 플레이어 처치→부활이 담당한다. 게이지는 화면 실체 비율을 그대로 따라온다.
public sealed class SectorBattleManager : IDisposable
{
    private const float LogInterval = 2f; // 검증 로그 주기(설정 대상 아님).
    private const float VisibleTotalEpsilon = 0.5f;
    // 허브 선정 hysteresis: 직전 허브의 분할 점수가 최적의 이 비율 이상이면 그대로 유지한다.
    // 근소 차로 매 틱 인접 칸으로 튀는 허브 진동을 흡수하되, 명확히 더 좋은 절단점이 나오면 양보한다.
    private const float HubHysteresisRatio = 0.8f;

    // 튜닝값은 SO_SectorBattle_Settings에서 주입(없으면 기본값 폴백).
    private readonly SO_SectorBattle_Settings _settings;
    private int   LiveCapTotal      => _settings != null ? _settings.LiveCapTotal      : 200;
    private float CaptureThreshold  => _settings != null ? _settings.CaptureThreshold  : 0.9f;
    private float MutationImmunityDuration => _settings != null ? _settings.MutationImmunityDuration : 3f;
    private int   MutationBurstThreshold => _settings != null ? _settings.MutationBurstThreshold : 5;
    private float SupportPowerRatio => _settings != null ? _settings.SupportPowerRatio : 0.2f;
    private float SupportDistanceFalloff => _settings != null ? _settings.SupportDistanceFalloff : 0.5f;
    // (A) 통합 점령 압력 튜닝.
    private float TopoShare             => _settings != null ? Mathf.Clamp01(_settings.TopoShare) : 0.5f;
    private float ConquestFractionPerSec => _settings != null ? _settings.ConquestFractionPerSec : 0.015f;

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
    private Sector _prevPlayerSector; // 직전 틱의 플레이어 섹터. 이탈 핸드오프 때 떠난 섹터에 안정화 유예를 준다.

    // 링크(연결 컴포넌트) BFS 재사용 버퍼(매 틱 new 방지).
    private readonly HashSet<Sector> _linkVisited = new();
    private readonly Queue<Sector> _linkQueue = new();
    private readonly List<SectorBattleState> _linkComponent = new();
    private readonly HashSet<Sector> _hubVisited = new();
    private int _maxLink = 1; // 이번 틱 최대 링크 크기(위상 압력 정규화 분모). RecomputeLinks가 갱신.

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

    // 배경 섹터: (A) 위상(영역 크기)과 전투력을 합성한 단일 점령 압력.
    private void TickSector(SectorBattleState state, float dt)
    {
        TickConquest(state, dt);
        UpdateOwnership(state);
    }

    // ── (A) 통합 점령 압력 ─────────────────────────────────────────────────────────
    // 위상(net=링크 영역 크기)과 전투력(aP-eP)을 각각 [-1,1]로 정규화해 단일 압력으로 합성한다.
    // 둘이 같은 방향이면 가속, 반대면 차감(상쇄 아닌 합성) — 변이+싸움이 같은 변수를 반대로 밀던 문제 해소.
    // net만으로도 압력이 작동하므로(공존 게이팅 없음) 적 0인 100% 칸도 큰 적 영역에 인접하면 직접 밀린다.
    private void TickConquest(SectorBattleState state, float dt)
    {
        if (state.TotalSum <= 0f) return;

        // 면역(100% 점령 직후 안정화 유예) — 잡몹 차원 배경 압력으로부터 점령지를 보호.
        // 플레이어가 찢어 점령한 성과가 떠나자마자 뺏기지 않게 하는 게 목적(긴 면역 = 결정타 유지 시간).
        // 단, 침략자(점령 진영의 반대편) 장수가 개입하면 면역을 뚫는다: 장수는 지루함을 깨는 와일드카드로,
        // 단신으로 면역 구역도 함락할 수 있다. 수비측 아군 장수는 자기편 보호막을 깨지 않는다.
        NavFaction invader = state.OwnerFaction == NavFaction.Ally ? NavFaction.Enemy : NavFaction.Ally;
        float invaderElite = invader == NavFaction.Ally ? state.AllyElitePower : state.EnemyElitePower;
        if (state.MutationImmunityTimer > 0f && invaderElite <= 0f)
        {
            state.MutationImmunityTimer -= dt;
            return;
        }

        float enemyward = ResolveConquestPressure(state); // +면 적이 민다(아군→적)
        // 전환량 = 압력 × 섹터 총병력 비율(규모 불변). 병력 400이든 4000이든 같은 비율 속도로 점령된다.
        ShiftZeroSum(state, -enemyward * ConquestFractionPerSec * state.TotalSum * dt);
    }

    // 단일 점령 압력 ∈ [-1,1]. +면 적 방향(AllyTotal 감소), -면 아군 방향. 로그 진단도 공유.
    //  · 위상: net(자기 영역 크기로 방어하는 링크 압력차)을 최대 링크 크기로 정규화·clamp.
    //  · 전투력: (아군 power − 적 power)를 합으로 나눈 상대 비율 정규화. 항상 [-1,1], 병력 규모에 불변.
    private float ResolveConquestPressure(SectorBattleState state)
    {
        float net = ResolveMutationNet(state); // +면 적 방향(아군→적)
        float topoNorm = Mathf.Clamp(net / Mathf.Max(1, _maxLink), -1f, 1f);

        float aP = state.AllyPower  + ResolveSupportPower(state, NavFaction.Ally);
        float eP = state.EnemyPower + ResolveSupportPower(state, NavFaction.Enemy);
        float pd = aP - eP; // +면 아군 우세
        // 상대 비율 정규화: 항상 [-1,1]이고 병력 규모에 불변(tanh 스케일 상수 불필요).
        float battleNorm = pd / Mathf.Max(1f, aP + eP);

        // 적 방향(+) = 위상 적 우세(topoNorm) + 전투력 적 우세(−battleNorm).
        float share = TopoShare;
        return Mathf.Clamp(share * topoNorm - (1f - share) * battleNorm, -1f, 1f);
    }

    // ── 링크(연결 컴포넌트) ────────────────────────────────────────────────────────
    // 매 틱: 점령 상태 판정 → 같은 진영 완전점령 섹터를 게이트로 묶어 링크 영향력(= 컴포넌트 크기)을 기록한다.
    // 경합 섹터는 링크에서 제외되어 단절점이 된다([[project_defender_dispersal]]의 절단점 = 연결부).
    private void RecomputeLinks()
    {
        int nextLinkId = 1;
        _maxLink = 1;
        foreach (KeyValuePair<Sector, SectorBattleState> kv in _states)
        {
            SectorBattleState s = kv.Value;
            s.WasLinkHub = s.IsLinkHub; // 이번 틱 리셋 전에 직전 허브 여부 보존(hysteresis 입력).
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
            if (influence > _maxLink) _maxLink = influence;
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
        SectorBattleState incumbent = null; // 직전 허브였던 후보 중 분할 점수 최고(hysteresis 후보).
        int incumbentScore = int.MinValue;

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

            if (candidate.WasLinkHub && splitScore > incumbentScore)
            {
                incumbent = candidate;
                incumbentScore = splitScore;
            }
        }

        // hysteresis: 직전 허브가 여전히 이 컴포넌트에 있고 분할 점수가 최적의 일정 비율 이상이면 유지(진동 흡수).
        if (incumbent != null && incumbent != best
            && incumbentScore >= Mathf.CeilToInt(bestSplitScore * HubHysteresisRatio))
            return incumbent;

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
        // 침략자(점령하려는 진영의 반대편) 장수가 그 섹터에 있으면 완전 점령을 확정하지 않는다(Contested 유지).
        // 게이지(잡몹 비율)는 100%까지 차오르되, 점령 확정·면역·링크 참여는 적 장수를 치우기 전까지 보류.
        // 장수가 도망·유인으로 빠지면 ElitePower가 0이 되어 곧바로 확정된다(위치 연동).
        if (n >= CaptureThreshold) return s.EnemyElitePower > 0f ? SectorControl.Contested : SectorControl.Ally;
        if (n <= 1f - CaptureThreshold) return s.AllyElitePower > 0f ? SectorControl.Contested : SectorControl.Enemy;
        return SectorControl.Contested;
    }

    // ── (A) 플레이어 섹터 점령 압력 ────────────────────────────────────────────────
    // 배경 TickConquest와 같은 enemyward 압력·같은 속도 모델을 쓰되, 적용만 실체 toggle(MutateAgents).
    // 아군/플레이어 우세(enemyward ≤ 0)면 적 주입을 정지한다 — 아군화(적→아군)는 오직 플레이어 처치→부활이
    // 담당한다("플레이어가 유일한 결정타"). 플레이어가 싸워 AllyTotal을 올리면 aP↑→battleNorm↑→enemyward가
    // 음수로 기울어 적 주입이 멎으므로, 인접 적에 둘러싸인 섹터도 100% 점령에 도달할 수 있다.
    private void TickPlayerConquest(SectorBattleState state, float dt)
    {
        if (state.TotalSum <= 0f) { state.MutationAccum = 0f; return; }

        // 면역(100% 점령 직후 안정화 유예) — 배경 TickConquest와 동일 통로.
        // 침략자(점령 진영의 반대편) 장수가 개입하면 면역을 뚫는다(수비 아군 장수는 자기편 보호막을 안 깬다).
        NavFaction invader = state.OwnerFaction == NavFaction.Ally ? NavFaction.Enemy : NavFaction.Ally;
        float invaderElite = invader == NavFaction.Ally ? state.AllyElitePower : state.EnemyElitePower;
        if (state.MutationImmunityTimer > 0f && invaderElite <= 0f)
        {
            state.MutationImmunityTimer -= dt;
            state.MutationAccum = 0f;
            return;
        }

        // 현장 우선 게이팅: 플레이어가 충분히 점령(게이지 ≥ CaptureThreshold)한 섹터는 적 주입을 정지한다.
        // 거시 위상(topoNorm)·지원(support) 압력이 플레이어의 실제 현장 성과를 덮지 못하게 한다 — 인접 거대
        // 적 영역에 둘러싸여(topoNorm=1) ew가 +로 유지돼도, 플레이어가 제압한 섹터는 100%까지 안정화된다.
        if (state.GaugeNormalized >= CaptureThreshold) { state.MutationAccum = 0f; return; }

        float enemyward = ResolveConquestPressure(state); // +면 적이 민다(아군→적)
        // 적이 미는 동안만(enemyward>0) 누적해 배출한다. 아군/거시 우세 구간은 누적을 0에서 멈춰(음수 빚 방지)
        // 자연히 주입이 안 된다 — 게이지 게이팅이 상한, 이 0 바닥이 하한을 맡아 별도 정지 분기가 필요 없다.
        // 아군화(적→아군)는 오직 플레이어 처치→부활이 담당한다("플레이어가 유일한 결정타").
        state.MutationAccum = Mathf.Max(0f, state.MutationAccum + enemyward * ConquestFractionPerSec * state.TotalSum * dt);
        int threshold = Mathf.Max(1, MutationBurstThreshold);
        if (state.MutationAccum < threshold) return;

        int burst = (int)state.MutationAccum;
        state.MutationAccum -= burst;
        MutateAgents(NavFaction.Ally, burst); // 아군→적
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

        // ① (A) 통합 점령 압력으로 화면 실체를 toggle한다 — 배경과 동일 압력, 적용만 실체 toggle.
        TickPlayerConquest(state, dt);

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

        // (A) 통합 압력 정규화 스케일 실측용 분포 집계.
        //  · net 범위/|net|max  → NormalizeTopo 분모(위상 압력 포화점, 보통 maxLink에 근접).
        //  · 전선 칸(양 진영 공존) powerDiff 전형값 → NormalizeBattle tanh scale.
        int active = 0, front = 0, maxLink = 0;
        float netMin = float.MaxValue, netMax = float.MinValue, absNetMax = 0f, absNetSum = 0f;
        float pdMin = float.MaxValue, pdMax = float.MinValue, absPdMax = 0f, absPdSum = 0f;
        float frontAbsPdSum = 0f, frontAbsPdMax = 0f;

        foreach (KeyValuePair<Sector, SectorBattleState> kv in _states)
        {
            SectorBattleState s = kv.Value;
            if (s.TotalSum <= 0f) continue;

            // 진단: [점령상태 L링크영향력 net변이압력 aP아군전투력 eP적전투력 게이지%]  (전투력=현지Power+지원Power)
            float net = ResolveMutationNet(s);
            float aP = s.AllyPower + ResolveSupportPower(s, NavFaction.Ally);
            float eP = s.EnemyPower + ResolveSupportPower(s, NavFaction.Enemy);
            float pd = aP - eP;

            active++;
            if (s.LinkInfluence > maxLink) maxLink = s.LinkInfluence;
            netMin = Mathf.Min(netMin, net); netMax = Mathf.Max(netMax, net);
            absNetMax = Mathf.Max(absNetMax, Mathf.Abs(net)); absNetSum += Mathf.Abs(net);
            pdMin = Mathf.Min(pdMin, pd); pdMax = Mathf.Max(pdMax, pd);
            absPdMax = Mathf.Max(absPdMax, Mathf.Abs(pd)); absPdSum += Mathf.Abs(pd);

            bool isFront = s.AllyTotal > 0f && s.EnemyTotal > 0f; // 양 진영 공존 = 실제 전선
            if (isFront)
            {
                front++;
                frontAbsPdSum += Mathf.Abs(pd);
                frontAbsPdMax = Mathf.Max(frontAbsPdMax, Mathf.Abs(pd));
            }

            if (shown < 12)
            {
                // ew = 통합 점령 압력(+면 적이 민다, −면 아군이 민다).
                float ew = ResolveConquestPressure(s);
                sb.Append($"{s.Sector.DisplayName}[{s.Control} L{s.LinkInfluence} net{net:0.0} ew{ew:+0.00;-0.00} aP{aP:0} eP{eP:0} {s.GaugeNormalized * 100f:0}%] ");
                shown++;
            }
        }

        if (active == 0) return;

        Debug.Log(sb.ToString());
        Debug.Log($"[SectorBattle/Dist] n={active} front={front} maxLink={maxLink} " +
                  $"| net[{netMin:0.0}..{netMax:0.0}] |net|avg{absNetSum / active:0.00} |net|max{absNetMax:0.0} " +
                  $"| pDiff[{pdMin:0}..{pdMax:0}] |pd|avg{absPdSum / active:0} |pd|max{absPdMax:0} " +
                  $"| frontPd avg{(front > 0 ? frontAbsPdSum / front : 0f):0} max{frontAbsPdMax:0}");
    }
}
