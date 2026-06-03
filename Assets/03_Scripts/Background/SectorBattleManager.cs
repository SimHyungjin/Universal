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
    private const float FullControlDisplayThreshold = 0.995f;
    private const float VisibleTotalEpsilon = 0.5f;

    // 튜닝값은 SO_SectorBattle_Settings에서 주입(없으면 기본값 폴백).
    private readonly SO_SectorBattle_Settings _settings;
    private int   LiveCapTotal      => _settings != null ? _settings.LiveCapTotal      : 200;
    private float EliteBasePower    => _settings != null ? _settings.EliteBasePower    : 30f;
    private float ElitePowerBonusPerHostileTotal => _settings != null ? _settings.ElitePowerBonusPerHostileTotal : 1f;
    private float ElitePowerBonusReferenceTotal => _settings != null ? _settings.ElitePowerBonusReferenceTotal : 100f;
    private float ElitePowerBonusExponent => _settings != null ? _settings.ElitePowerBonusExponent : 2f;
    private float ElitePowerBonusMaxRatio => _settings != null ? _settings.ElitePowerBonusMaxRatio : 0.35f;
    private float EncroachRate      => _settings != null ? _settings.EncroachRate      : 0.05f;
    private float EncroachMaxPerSec => _settings != null ? _settings.EncroachMaxPerSec : 3f;
    private float CaptureThreshold  => _settings != null ? _settings.CaptureThreshold  : 0.95f;
    private float PressureDeadzone => _settings != null ? _settings.PressureDeadzone : 0.015f;
    private float PressureDecisiveAdvantage => _settings != null ? _settings.PressureDecisiveAdvantage : 0.32f;
    private float PressureCurve => _settings != null ? _settings.PressureCurve : 1.25f;
    private float PressureDecayRate => _settings != null ? _settings.PressureDecayRate : 0.45f;
    private float PressureMoveThreshold => _settings != null ? _settings.PressureMoveThreshold : 0.08f;
    private float PressureMoveCurve => _settings != null ? _settings.PressureMoveCurve : 1.15f;
    private float ControlBiasStrength => _settings != null ? _settings.ControlBiasStrength : 0.18f;
    private float FrontTurbulenceStrength => _settings != null ? _settings.FrontTurbulenceStrength : 0.22f;
    private float FrontTurbulencePowerFalloff => _settings != null ? _settings.FrontTurbulencePowerFalloff : 0.75f;

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
    private Sector _polledSector;     // 플레이어 섹터 폴링 기준. 섹터가 바뀌면 LiveCount를 재동기화한다.
    private Sector _lastPlayerSector; // 직전 플레이어 섹터. 떠난 섹터의 화면 수를 0으로 비우는 데 쓴다.

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

    private static void SeedTotal(SectorBattleState state, NavAgentSpawnEntry[] spawns)
    {
        if (spawns == null) return;

        for (int i = 0; i < spawns.Length; i++)
        {
            NavAgentSpawnEntry entry = spawns[i];
            if (entry.Data == null || entry.Count <= 0) continue;
            state.AddTotal(entry.Faction, entry.Count * ResolveUnitSectorPower(entry.Data));
        }
    }

    private static float ResolveUnitSectorPower(SO_Unit_Data data)
    {
        float power = data != null && data.StatsData != null ? data.StatsData.SectorPower : 1f;
        return power > 0f ? power : 0f;
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

        Sector playerSector = _sectorManager != null ? _sectorManager.CurrentSector : null;

        // 플레이어가 섹터를 떠났으면 그 섹터의 화면 수를 0으로 비운다(Total은 제로섬이라 그대로 보존).
        if (_lastPlayerSector != playerSector)
        {
            ClearLive(_lastPlayerSector);
            _lastPlayerSector = playerSector;
            _polledSector = null; // 새 섹터에서 LiveCount 재동기화.
        }

        foreach (KeyValuePair<Sector, SectorBattleState> kv in _states)
        {
            SectorBattleState state = kv.Value;
            if (state.Sector == playerSector)
                TickPlayerSector(state, dt);
            else
                TickSector(state, dt);
        }
    }

    private void ClearLive(Sector sector)
    {
        if (sector == null || !_states.TryGetValue(sector, out SectorBattleState s)) return;
        s.AllyLiveCount = 0;
        s.EnemyLiveCount = 0;
    }

    // 매 틱 섹터별 엘리트 전력을 다시 합산한다(엘리트는 이동/생사하므로 매번 갱신). 엘리트=전력 가산 가속기.
    private void AccumulateElitePower()
    {
        foreach (KeyValuePair<Sector, SectorBattleState> kv in _states)
        {
            kv.Value.AllyElitePower = 0f;
            kv.Value.EnemyElitePower = 0f;
            kv.Value.AllyEliteAttritionPower = 0f;
            kv.Value.EnemyEliteAttritionPower = 0f;
        }

        IReadOnlyList<Elite_State> elites = Elite_Manager.Instance != null ? Elite_Manager.Instance.Elites : null;
        if (elites == null) return;

        for (int i = 0; i < elites.Count; i++)
        {
            Elite_State e = elites[i];
            if (e == null || !e.IsAlive || e.CurrentSector == null) continue;
            if (!_states.TryGetValue(e.CurrentSector, out SectorBattleState s)) continue;

            float hostileTotal = e.Faction == NavFaction.Ally ? s.EnemyTotal : s.AllyTotal;
            float baseElitePower = ResolveBaseElitePower(e);
            float elitePower = ResolveElitePower(baseElitePower, hostileTotal);
            if (e.Faction == NavFaction.Ally)
            {
                s.AllyElitePower += elitePower;
                s.AllyEliteAttritionPower += baseElitePower;
            }
            else
            {
                s.EnemyElitePower += elitePower;
                s.EnemyEliteAttritionPower += baseElitePower;
            }
        }
    }

    // 배경 섹터: 전력 우세 진영이 열세 진영을 시간당 조금씩 잠식한다(제로섬, 전력차 비례·상한).
    private float ResolveBaseElitePower(Elite_State elite)
    {
        SO_Character_Data character = elite != null && elite.Data != null ? elite.Data.Character : null;
        return SectorPowerFormula.Calculate(character, EliteBasePower);
    }

    private float ResolveElitePower(float basePower, float hostileTotal)
    {
        if (basePower <= 0f || hostileTotal <= 0f || ElitePowerBonusPerHostileTotal <= 0f || ElitePowerBonusMaxRatio <= 0f)
            return basePower;

        float referenceTotal = Mathf.Max(1f, ElitePowerBonusReferenceTotal);
        float exponent = Mathf.Max(0.01f, ElitePowerBonusExponent);
        float density = Mathf.Max(0f, hostileTotal / referenceTotal);
        float curvedHostileTotal = hostileTotal * Mathf.Pow(density, exponent - 1f);
        float bonus = curvedHostileTotal * ElitePowerBonusPerHostileTotal;
        float maxBonus = basePower * ElitePowerBonusMaxRatio;
        return basePower + Mathf.Min(bonus, maxBonus);
    }

    private void TickSector(SectorBattleState state, float dt)
    {
        SnapDisplayedFullControl(state);

        // 양 진영 전력(엘리트 포함)이 맞붙고 옮길 잡몹이 있으면 잠식. 한 진영만 100% 점령한 섹터는
        // 상대 전력이 들어오기 전까지 정적(아군 엘리트가 적색 섹터에 들어오면 AllyPower>0이 되어 잠식 시작).
        if (state.TotalSum > 0f && state.AllyPower > 0f && state.EnemyPower > 0f)
        {
            // 전력차(엘리트 포함)로 잠식 방향·속도를 정하되, 실제 이동은 잡몹 병력만(엘리트는 가속기).
            TickBackgroundPressure(state, dt);
        }
        else
        {
            DecayPressure(state, dt);
        }

        UpdateOwnership(state);
    }

    // 플레이어 섹터: 실제 NavAgent 사망을 폴링해 제로섬 이동시키고, 화면을 비율 목표까지 보충한다.
    // 진입/전환 중(스폰 진행 중)에는 사망/보충 없이 LiveCount만 동기화해 거짓 사망을 막는다.
    private void TickBackgroundPressure(SectorBattleState state, float dt)
    {
        float totalPower = state.AllyPower + state.EnemyPower;
        if (totalPower <= 0f)
        {
            DecayPressure(state, dt);
            return;
        }

        float advantage = ResolveEffectiveAdvantage(state, totalPower);
        float targetPressure = ResolvePressureTarget(advantage);
        MovePressureToward(state, targetPressure, dt);

        float pressure = state.ControlPressure;
        float absPressure = Mathf.Abs(pressure);
        if (absPressure <= PressureMoveThreshold)
            return;

        float t = Mathf.InverseLerp(PressureMoveThreshold, 1f, absPressure);
        float speed = EncroachMaxPerSec * Mathf.Pow(t, PressureMoveCurve) * Mathf.Sign(pressure);
        ShiftTotalToAlly(state, speed * dt);
    }

    private float ResolvePressureTarget(float advantage)
    {
        float absAdvantage = Mathf.Abs(advantage);
        if (absAdvantage <= PressureDeadzone)
            return 0f;

        float t = Mathf.InverseLerp(
            PressureDeadzone,
            Mathf.Max(PressureDeadzone + 0.01f, PressureDecisiveAdvantage),
            absAdvantage);
        return Mathf.Sign(advantage) * Mathf.Pow(t, PressureCurve);
    }

    private float ResolveEffectiveAdvantage(SectorBattleState state, float totalPower)
    {
        float powerAdvantage = (state.AllyPower - state.EnemyPower) / totalPower;
        float controlBias = (state.GaugeNormalized - 0.5f) * 2f * ControlBiasStrength;
        float turbulence = ResolveFrontTurbulence(state, powerAdvantage);
        return Mathf.Clamp(powerAdvantage + controlBias + turbulence, -1f, 1f);
    }

    private float ResolveFrontTurbulence(SectorBattleState state, float powerAdvantage)
    {
        float parity = 1f - Mathf.Clamp01(Mathf.Abs(powerAdvantage) / FrontTurbulencePowerFalloff);
        if (parity <= 0f)
            return 0f;

        float seed = SectorSeed(state.Sector);
        float slow = Mathf.PerlinNoise(seed, Time.time * 0.035f) * 2f - 1f;
        float wave = Mathf.Sin(Time.time * 0.11f + seed * 6.28318f);
        float noise = Mathf.Clamp(slow * 0.75f + wave * 0.25f, -1f, 1f);
        return noise * FrontTurbulenceStrength * parity;
    }

    private static float SectorSeed(Sector sector)
    {
        int hash = sector != null ? sector.GetInstanceID() : 0;
        unchecked
        {
            hash ^= hash << 13;
            hash ^= hash >> 17;
            hash ^= hash << 5;
        }
        return (hash & 0xffff) / 65535f * 37.17f + 0.11f;
    }

    private void MovePressureToward(SectorBattleState state, float targetPressure, float dt)
    {
        if (Mathf.Abs(targetPressure) <= 0.0001f)
        {
            DecayPressure(state, dt);
            return;
        }

        float buildRate = Mathf.Max(0.01f, EncroachRate * 14f);
        bool reversing = Mathf.Abs(state.ControlPressure) > 0.0001f
                         && Mathf.Sign(state.ControlPressure) != Mathf.Sign(targetPressure);
        float rate = reversing ? buildRate + PressureDecayRate : buildRate;
        state.ControlPressure = Mathf.MoveTowards(state.ControlPressure, targetPressure, rate * dt);
    }

    private void DecayPressure(SectorBattleState state, float dt)
        => state.ControlPressure = Mathf.MoveTowards(state.ControlPressure, 0f, PressureDecayRate * dt);

    private void TickPlayerSector(SectorBattleState state, float dt)
    {
        CountLiveAgents(out int curAlly, out int curEnemy);

        bool transitioning = _sectorManager != null && _sectorManager.IsTransitioning;
        bool resync = transitioning || _polledSector != state.Sector;

        if (resync)
        {
            state.AllyLiveCount  = curAlly;
            state.EnemyLiveCount = curEnemy;
            _polledSector = transitioning ? null : state.Sector;
        }
        else
        {
            // 직전보다 줄어든 만큼이 사망. 적이 죽으면 아군 쪽으로, 아군이 죽으면 적 쪽으로 제로섬 이동.
            int allyDeaths  = Mathf.Max(0, state.AllyLiveCount  - curAlly);
            int enemyDeaths = Mathf.Max(0, state.EnemyLiveCount - curEnemy);
            int net = enemyDeaths - allyDeaths; // +면 아군 증가
            if (net != 0)
                ShiftTotalToAlly(state, net);

            state.AllyLiveCount  = curAlly;
            state.EnemyLiveCount = curEnemy;

            // 화면을 비율 목표까지 보충(부족분만 스폰).
            ResolveLiveTargets(state, out int allyTarget, out int enemyTarget);
            state.AllyLiveCount  += ReplenishTo(state, NavFaction.Ally,  curAlly,  allyTarget);
            state.EnemyLiveCount += ReplenishTo(state, NavFaction.Enemy, curEnemy, enemyTarget);
        }

        DecayPressure(state, dt);
        UpdateOwnership(state);
    }

    // 아군 쪽으로 delta만큼 제로섬 이동(합 보존). delta<0이면 적 쪽으로.
    private static void ShiftTotalToAlly(SectorBattleState state, float delta)
    {
        float sum = state.TotalSum;
        state.AllyTotal  = Mathf.Clamp(state.AllyTotal + delta, 0f, sum);
        state.EnemyTotal = sum - state.AllyTotal;
        SnapDisplayedFullControl(state);
    }

    private static bool HasVisibleTotal(SectorBattleState state, NavFaction faction)
        => state != null && state.TotalOf(faction) > VisibleTotalEpsilon;

    private static void SnapDisplayedFullControl(SectorBattleState state)
    {
        if (state == null)
            return;

        float sum = state.TotalSum;
        if (sum <= 0f)
            return;

        float ally = Mathf.Max(0f, state.AllyTotal);
        float enemy = Mathf.Max(0f, state.EnemyTotal);
        float n = ally / sum;

        if ((n >= FullControlDisplayThreshold || enemy <= VisibleTotalEpsilon) && state.EnemyElitePower <= 0f)
        {
            state.AllyTotal = sum;
            state.EnemyTotal = 0f;
            state.ControlPressure = 0f;
            return;
        }

        if ((n <= 1f - FullControlDisplayThreshold || ally <= VisibleTotalEpsilon) && state.AllyElitePower <= 0f)
        {
            state.AllyTotal = 0f;
            state.EnemyTotal = sum;
            state.ControlPressure = 0f;
        }
    }

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

    // 살아있는(죽음 연출 중이 아닌) NavAgent를 진영별로 센다. 모든 NavAgent는 플레이어 섹터에만 존재한다.
    private static void CountLiveAgents(out int ally, out int enemy)
    {
        ally = 0; enemy = 0;
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        EntityManager em = world.EntityManager;
        EntityQuery query = em.CreateEntityQuery(
            ComponentType.ReadOnly<NavAgentFaction>(),
            ComponentType.ReadOnly<NavAgentDeath>());
        NativeArray<NavAgentFaction> factions = query.ToComponentDataArray<NavAgentFaction>(Allocator.Temp);
        NativeArray<NavAgentDeath> deaths = query.ToComponentDataArray<NavAgentDeath>(Allocator.Temp);

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

        Sector playerSector = _sectorManager != null ? _sectorManager.CurrentSector : null;
        var sb = new System.Text.StringBuilder("[SectorBattle] ");
        int shown = 0;
        foreach (KeyValuePair<Sector, SectorBattleState> kv in _states)
        {
            SectorBattleState s = kv.Value;
            if (s.Sector == playerSector) continue;
            if (s.AllyTotal <= 0f && s.EnemyTotal <= 0f) continue;

            sb.Append($"{s.Sector.DisplayName}(A{s.AllyTotal:0} E{s.EnemyTotal:0} {s.GaugeNormalized * 100f:0}% {s.OwnerFaction}) ");
            if (++shown >= 6) break;
        }

        // if (shown > 0)
        //     Debug.Log(sb.ToString());
    }
}
