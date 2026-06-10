using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MapNav.Core;
using MapNav.Ecs;
using Unity.Mathematics;
using UnityEngine;

public sealed class Elite_Manager : IDisposable
{
    private const float PlayerGateCrossingRange = 4f;

    private readonly List<Elite_State> _elites = new();
    private readonly Dictionary<Elite_State, Hud_GameScene_Minimap.Marker> _markers = new();
    private readonly SectorManager _sectorManager;
    private readonly Elite_WorldSimulator _worldSimulator;
    private readonly SectorBattleManager _sectorBattleManager;
    private readonly SO_SectorBattle_Settings _settings;

    private Hud_GameScene_Minimap _minimap;
    private CancellationTokenSource _embodyCts = new();
    private CancellationTokenSource _worldCts = new();

    // 본진 결전: 플레이어가 본진(EnemyHome)에 진입하면 소집 시작. _siegeWon은 승리 로그 1회성 가드.
    private Sector _capitalSector;
    private bool _siegeWon;

    public static Elite_Manager Instance { get; private set; }
    public IReadOnlyList<Elite_State> Elites => _elites;

    public Elite_Manager(
        SectorManager sectorManager = null,
        MinimapModel map = null,
        SectorBattleManager sectorBattleManager = null,
        SO_SectorBattle_Settings settings = null)
    {
        _sectorManager = sectorManager != null ? sectorManager : SectorManager.Instance;
        _settings = settings;
        _worldSimulator = new Elite_WorldSimulator(map, settings);
        _sectorBattleManager = sectorBattleManager != null ? sectorBattleManager : SectorBattleManager.Instance;
        Instance = this;

        if (_sectorManager != null)
        {
            _sectorManager.SectorChanged += OnSectorChanged;
            RefreshEmbodiments();
        }

        RunWorldSimulationAsync(_worldCts.Token).Forget();
    }

    public Elite_State Register(
        SO_Elite_Data data,
        Sector sector,
        Vector3 worldPosition,
        Vector3 forward,
        NavFaction faction)
    {
        var state = new Elite_State(data, sector, worldPosition, forward, faction);
        _elites.Add(state);
        AddMinimapMarker(state);
        RefreshEmbodiments();
        return state;
    }

    public void Unregister(Elite_State state)
    {
        if (state == null || !_elites.Remove(state)) return;

        RemoveMinimapMarker(state);
        ReleaseEmbodiment(state);
    }

    public void BindMinimap(Hud_GameScene_Minimap minimap)
    {
        if (_minimap == minimap) return;

        ClearMinimapMarkers();
        _minimap = minimap;

        for (int i = 0; i < _elites.Count; i++)
            AddMinimapMarker(_elites[i]);
    }

    public void Dispose()
    {
        if (Instance == this)
            Instance = null;

        if (_sectorManager != null)
            _sectorManager.SectorChanged -= OnSectorChanged;

        _worldCts?.Cancel();
        _worldCts?.Dispose();
        _worldCts = null;

        CancelPendingEmbodiments();
        ClearMinimapMarkers();

        for (int i = 0; i < _elites.Count; i++)
            ReleaseEmbodiment(_elites[i]);

        _elites.Clear();
    }

    public void SetWorldMap(MinimapModel map)
    {
        _worldSimulator.SetMap(map);
        RefreshEmbodiments();
    }

    private void OnSectorChanged(Sector sector)
    {
        UpdateCapitalSiege(sector);
        RefreshEmbodiments();
    }

    // 본진(적 코어) 결전 제어. 플레이어가 본진에 들어가면 살아있는 본진 소유 진영 엘리트를 전원 소집한다.
    // 본진을 떠나면 소집 해제(재진입 시 재개). 게이트 잠금·패배·결과 화면은 이후 슬라이스.
    public void SetCapital(Sector capital) => _capitalSector = capital;

    private void UpdateCapitalSiege(Sector sector)
    {
        if (_capitalSector == null) return;

        if (sector == _capitalSector)
        {
            NavFaction owner = NavFaction.Enemy;
            if (_sectorBattleManager != null && _sectorBattleManager.TryGetState(_capitalSector, out SectorBattleState s))
                owner = s.OwnerFaction;
            _worldSimulator.SetRally(_capitalSector, owner);
            _siegeWon = false;
        }
        else
        {
            _worldSimulator.ClearRally();
        }
    }

    // 승리 판정(간이): 소집 활성 중 살아있는 소집 진영 엘리트가 0이 되면 1회 로그하고 소집을 닫는다.
    private void TickCapitalSiege()
    {
        if (!_worldSimulator.IsRallying) return;

        NavFaction faction = _worldSimulator.RallyFaction;
        for (int i = 0; i < _elites.Count; i++)
        {
            Elite_State e = _elites[i];
            if (e != null && e.IsAlive && e.Faction == faction)
                return; // 아직 소집 진영 엘리트가 남아 있다.
        }

        if (!_siegeWon)
        {
            _siegeWon = true;
            Debug.Log("[CapitalSiege] 결전 승리 — 본진 적 엘리트 전멸");
        }
        _worldSimulator.ClearRally();
    }

    // 시작 엘리트를 진영 점령 섹터들에 라운드로빈으로 분산 배치한다(영역 전체에 펼쳐 지키게 — 본진 1곳 집중 대신).
    // 이후 매크로 AI(Elite_WorldSimulator)가 첫 think에 각자 역할 위치(Defender=허브, Vanguard=전선 등)로 정렬한다.
    public void SeedStartRoster(EliteSpawnEntry[] roster, IReadOnlyList<Sector> sectors, NavFaction faction)
    {
        if (roster == null || sectors == null || sectors.Count == 0) return;

        int totalCount = CountSpawnEntries(roster);
        int spawnIndex = 0;
        for (int i = 0; i < roster.Length; i++)
        {
            EliteSpawnEntry entry = roster[i];
            if (entry.Data == null || entry.Count <= 0) continue;

            for (int j = 0; j < entry.Count; j++)
            {
                Sector sector = sectors[spawnIndex % sectors.Count];
                Vector3 position = ResolveSectorSpawnPosition(sector, entry.Data, spawnIndex, totalCount);
                Vector3 forward = sector.transform.forward.sqrMagnitude > 0.0001f
                    ? sector.transform.forward
                    : Vector3.forward;
                Elite_State state = Register(entry.Data, sector, position, forward, faction);
                state.FieldThinkTimer = ResolveInitialThinkDelay(spawnIndex, totalCount);
                spawnIndex++;
            }
        }
    }

    private static Vector3 ResolveSectorSpawnPosition(Sector sector, SO_Elite_Data data, int index, int count)
    {
        Vector3 center = sector != null ? sector.transform.position : Vector3.zero;
        if (count <= 1)
            return ResolveNavSafePosition(sector, center, ResolveAgentRadius(data));

        float angle = index * 137.50776f * Mathf.Deg2Rad;
        float radius = Mathf.Min(5.5f, 1.6f + Mathf.Sqrt(index) * 0.75f);
        Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
        return ResolveNavSafePosition(sector, center + offset, ResolveAgentRadius(data));
    }

    private static int CountSpawnEntries(EliteSpawnEntry[] entries)
    {
        int count = 0;
        if (entries == null)
            return count;

        for (int i = 0; i < entries.Length; i++)
        {
            EliteSpawnEntry entry = entries[i];
            if (entry.Data != null && entry.Count > 0)
                count += entry.Count;
        }

        return count;
    }

    private static float ResolveInitialThinkDelay(int index, int count)
        => count > 1 ? 0.15f + (index % 8) * 0.1f : 0f;

    private void RefreshEmbodiments()
    {
        CancelPendingEmbodiments();
        _embodyCts = new CancellationTokenSource();
        RefreshEmbodimentsAsync(_embodyCts.Token).Forget();
    }

    private async UniTaskVoid RefreshEmbodimentsAsync(CancellationToken token)
    {
        Sector current = _sectorManager != null ? _sectorManager.CurrentSector : null;

        for (int i = 0; i < _elites.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            Elite_State state = _elites[i];
            if (state == null || !state.IsAlive)
            {
                ReleaseEmbodiment(state);
                continue;
            }

            TryBeginObservedGateArrival(state, current);

            if (state.CurrentSector != current)
            {
                // 실체화 엘리트가 게이트 이동 중(대쉬 또는 걷는 단계)이면 섹터가 달라도 해제하지 않는다.
                // FinalizeGateExit이 대쉬 완료 후 CurrentSector를 갱신하고 재실체화를 처리한다.
                bool committedToGate = state.Embodiment != null
                    && (state.IsGateEntryAnimating || state.PendingExitSector != null);
                if (!committedToGate)
                {
                    ReleaseEmbodiment(state, preservePendingExit: true, observedSector: current);
                    if (state.CurrentSector != current)
                        continue;
                }
            }

            if (state.Embodiment != null)
                continue;

            if (state.IsApproachingGate || state.IsFieldTraveling)
                continue;

            await EmbodyAsync(state, token);
        }
    }

    private static async UniTask EmbodyAsync(Elite_State state, CancellationToken token)
    {
        if (state?.Data == null) return;

        // 장수는 CharacterController+중력으로 수직 처리하므로 스폰 y가 지면이 아니면 떨어진다.
        // 섹터 중앙 좌표를 현재 섹터 nav 그래프의 지면 높이로 스냅해 그라운드 상태로 스폰한다.
        bool wasFieldDeparting = state.FieldDestinationSector != null && !state.IsApproachingGate;
        Sector fieldDepartureDestination = wasFieldDeparting ? state.FieldDestinationSector : null;
        Vector3 arrival = wasFieldDeparting
            ? Elite_WorldSimulator.ResolveGateDeparturePosition(
                state.CurrentSector,
                fieldDepartureDestination,
                state)
            : ResolveNavSafePosition(state.CurrentSector, state.WorldPosition, ResolveAgentRadius(state.Data));
        state.WorldPosition = arrival;

        // 게이트 진입이면 반대편(출발 섹터) 게이트에서 스폰해 도착 지점까지 대쉬한다(반대 섹터에서 오는 느낌).
        bool dashIn = state.Presence == ElitePresenceState.GateArriving;

        Vector3 spawnPosition = dashIn ? state.GateEntryStart : arrival;
        Vector3 facing = dashIn ? (arrival - state.GateEntryStart) : state.Forward;
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.0001f) facing = state.Forward;

        GameObject go = await CharacterSpawner.SpawnPrefabAsync(
            state.Data,
            new SpawnRequest(spawnPosition, facing, state.CurrentSector),
            token);

        if (go == null) return;

        // 매크로 이동 중(Moving/GateApproach)에 실체화되는 경우 Destination을 클리어한다.
        // 클리어하지 않으면 TryGetTransition이 FieldDestinationSector를 읽어 마커가 이동 중 표시를 유지한다.
        // GateArriving(게이트 대쉬 진입 연출 중)은 FieldDestination이 이미 null이므로 조건에 해당하지 않는다.
        bool wasGateApproaching = state.IsApproachingGate;
        Sector gateApproachDestination = wasGateApproaching ? state.GateApproachDestinationSector : null;
        bool wasFieldMoving = state.IsFieldTraveling || wasGateApproaching;
        state.AttachEmbodiment(go);
        if (wasGateApproaching && gateApproachDestination != null && gateApproachDestination != state.CurrentSector)
        {
            state.CancelFieldTravel();
            state.BeginEmbodiedGateExit(gateApproachDestination);
        }
        else if (wasFieldDeparting && fieldDepartureDestination != null && fieldDepartureDestination != state.CurrentSector)
        {
            state.CancelFieldTravel();
            state.BeginEmbodiedGateExit(fieldDepartureDestination);
        }
        else if (wasFieldMoving)
        {
            state.CancelFieldTravel();
        }

        Character_ActionHandler actionHandler = go.GetComponent<Character_ActionHandler>();
        actionHandler?.SetCharacterData(state.Data != null ? state.Data.Character : null, clearEquippedLoadout: true);

        Elite_Embodiment embodiment = go.GetComponent<Elite_Embodiment>();
        if (embodiment == null)
            embodiment = go.AddComponent<Elite_Embodiment>();
        embodiment.Bind(state);

        // Embodiment를 Brain보다 먼저 Bind한다: Brain이 SetCommandSource로 AI 입력을 꽂기 전에
        // Vitals 진영/체력(Elite_State 기준)이 먼저 설정돼야 한다.
        // 이동은 Brain이 MapNavMonoAgent(steer-only)로 경로/방향을 계산해 Character_MoveController에 흘린다(Bind에서 설정).
        Elite_Brain brain = go.GetComponent<Elite_Brain>();
        if (brain == null)
            brain = go.AddComponent<Elite_Brain>();
        brain.Bind(state);

        // 게이트로 막 진입한 장수는 반대편 게이트에서 도착 지점까지 통과 대쉬(연출).
        // 대쉬는 공유 _embodyCts(token)가 아니라 몸체 수명(파괴 토큰)에 묶는다. token에 묶으면
        // 다른 장수 도착·플레이어 섹터 변경 등으로 RefreshEmbodiments가 재진입해 _embodyCts를 취소할 때
        // 진행 중인 대쉬까지 같이 취소돼, IsGateEntryAnimating이 조기 해제되고 미니맵 마커가
        // 게이트 글라이드 도중 노출된다. 몸체가 Release(파괴)될 때만 대쉬를 끊는 게 올바른 수명.
        if (dashIn)
            DashInFromGateAsync(state, go, brain, arrival, go.GetCancellationTokenOnDestroy()).Forget();
    }

    public void ForceGateCrossingWithPlayer(SectorGate departureGate, SectorGate arrivalGate)
    {
        if (departureGate == null || arrivalGate == null)
            return;

        Sector from = departureGate.Sector;
        Sector to = arrivalGate.Sector;
        if (from == null || to == null || from == to)
            return;

        Vector3 gatePosition = departureGate.SpawnPosition;
        float rangeSqr = PlayerGateCrossingRange * PlayerGateCrossingRange;

        for (int i = 0; i < _elites.Count; i++)
        {
            Elite_State state = _elites[i];
            if (state == null || !state.IsAlive || state.Embodiment == null)
                continue;
            if (state.CurrentSector != from)
                continue;

            Vector3 offset = state.Embodiment.transform.position - gatePosition;
            offset.y = 0f;
            bool closeEnough = offset.sqrMagnitude <= rangeSqr;
            bool alreadyGoingThere = state.PendingExitSector == to
                                     || state.GateApproachDestinationSector == to
                                     || state.FieldDestinationSector == to;
            if (!closeEnough && !alreadyGoingThere)
                continue;

            Elite_Brain brain = state.Embodiment.GetComponent<Elite_Brain>();
            state.CancelFieldTravel();
            state.BeginEmbodiedGateExit(to);

            if (closeEnough)
                BeginGateExitDash(state, brain, departureGate);
        }
    }

    private const float GateEntryArcHeight = 0.6f;
    private const float GateArrivalThinkDelayMin = 0.35f;
    private const float GateArrivalThinkDelayMax = 1.15f;

    // 게이트 통과 대쉬: 반대편 게이트(go 현재 위치)에서 도착 지점까지 transform을 직접 Lerp로 가로지른다
    // (CharacterController.Move는 게이트 벽/콜라이더에 막히므로 플레이어 대쉬처럼 직접 이동). 대쉬 동안 ActionHandler/Brain을
    // 끄고(중력·AI가 안 싸우게) 끝나면 복구한다. 취소·몸체 파괴 시 finally로 안전 복구(fake-null 자동 스킵).
    private static async UniTaskVoid DashInFromGateAsync(Elite_State state, GameObject go, Elite_Brain brain, Vector3 destination, CancellationToken token)
    {
        if (go == null) return;

        Transform tr = go.transform;
        Character_ActionHandler action = go.GetComponent<Character_ActionHandler>();
        Character_Vfx vfx = go.GetComponent<Character_Vfx>();

        Vector3 start = tr.position;
        Vector3 planar = destination - start;
        planar.y = 0f;
        Vector3 dir = planar.sqrMagnitude > 0.0001f ? planar.normalized : tr.forward;

        float speed = action != null ? action.GateTransitionSpeed : 18f;
        float duration = Mathf.Max(0.01f, planar.magnitude / Mathf.Max(0.01f, speed)); // 플레이어 게이트 대쉬와 동일 로직(거리/속도, 상한 없음 — SectorManager.ResolveDashDuration).

        bool brainWas = brain != null && brain.enabled;
        bool actionWas = action != null && action.enabled;
        if (brain != null) brain.enabled = false;
        action?.PrepareSectorGateTransition();
        if (action != null) action.enabled = false; // 직접 transform 이동(게이트 통과). 중력/CC가 안 싸우게.

        try
        {
            tr.rotation = Quaternion.LookRotation(dir, Vector3.up);
            vfx?.PlayDashStart(dir);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (go == null) return;
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);
                float arc = Mathf.Sin(t * Mathf.PI) * GateEntryArcHeight;
                tr.position = Vector3.Lerp(start, destination, eased) + Vector3.up * arc;
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            if (go == null) return;
            tr.position = destination;
            vfx?.PlayDashEnd(dir);
            action?.CompleteSectorGateTransition();
        }
        catch (OperationCanceledException) { }
        finally
        {
            bool ownsState = state != null && state.Embodiment == go;
            if (ownsState)
            {
                state.FinishGateArrival();
                state.FieldThinkTimer = Mathf.Max(state.FieldThinkTimer, ResolveGateArrivalThinkDelay(state));
            }

            bool releasedAfterArrival = false;
            if (ownsState
                && SectorManager.Instance != null
                && state.CurrentSector != SectorManager.Instance.CurrentSector)
            {
                ReleaseEmbodiment(state);
                releasedAfterArrival = true;
            }

            if (!releasedAfterArrival)
            {
                if (action != null && actionWas) action.enabled = true;
                if (brain != null) brain.enabled = brainWas;
            }
        }
    }

    // 실체화 엘리트가 게이트까지 걸어와 통과하는 출구 대쉬(진입의 대칭). Elite_Brain.TickGateExit가 호출.
    public void BeginGateExitDash(Elite_State state, Elite_Brain brain, SectorGate gate)
    {
        if (state == null || gate == null || state.Embodiment == null)
            return;
        if (state.IsGateEntryAnimating) // 이미 통과 대쉬 중이면 중복 시작 방지.
            return;

        if (gate.ConnectedGate == null || gate.ConnectedGate.Sector == null)
            return;

        state.BeginEmbodiedGateExitDash(gate.ConnectedGate.Sector);
        DashOutThroughGateAsync(state, state.Embodiment, brain, gate,
            state.Embodiment.GetCancellationTokenOnDestroy()).Forget();
    }

    // 게이트 이쪽(현재 위치)에서 반대편 도착 지점까지 transform을 직접 Lerp로 통과(nav 불필요 — 진입 대쉬와 동일).
    // 옆 섹터 GameObject·게이트는 씬에 이미 존재하므로 nav 로드 여부와 무관하게 그 도어웨이까지 실제로 이동한다.
    // 통과가 끝나면 FinalizeGateExit으로 몸체를 해제하고 목적지 섹터의 매크로로 인계한다. 취소(몸체 파괴) 시 인계 없이 종료.
    private async UniTaskVoid DashOutThroughGateAsync(Elite_State state, GameObject go, Elite_Brain brain, SectorGate gate, CancellationToken token)
    {
        if (go == null) return;

        Sector destination = gate != null && gate.ConnectedGate != null ? gate.ConnectedGate.Sector : null;
        if (destination == null)
        {
            if (state != null)
                state.CancelEmbodiedGateExit();
            return;
        }

        Vector3 doorway = Elite_WorldSimulator.ResolveGateExitArrival(state.CurrentSector, destination, state);
        doorway = ResolveNavSafePosition(destination, doorway, ResolveAgentRadius(state.Data));

        Transform tr = go.transform;
        Character_ActionHandler action = go.GetComponent<Character_ActionHandler>();
        Character_Vfx vfx = go.GetComponent<Character_Vfx>();
        bool brainWas = brain != null && brain.enabled;
        bool actionWas = action != null && action.enabled;

        Vector3 start = tr.position;
        Vector3 planar = doorway - start;
        planar.y = 0f;
        Vector3 dir = planar.sqrMagnitude > 0.0001f ? planar.normalized : tr.forward;

        float speed = action != null ? action.GateTransitionSpeed : 18f;
        float duration = Mathf.Max(0.01f, planar.magnitude / Mathf.Max(0.01f, speed)); // 플레이어 게이트 대쉬와 동일 로직(거리/속도, 상한 없음 — SectorManager.ResolveDashDuration).

        if (brain != null) brain.enabled = false;
        action?.PrepareSectorGateTransition();
        if (action != null) action.enabled = false; // 직접 transform 이동(게이트 통과). 중력/CC가 안 싸우게.

        bool completed = false;
        try
        {
            tr.rotation = Quaternion.LookRotation(dir, Vector3.up);
            vfx?.PlayDashStart(dir);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (go == null) return;
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);
                float arc = Mathf.Sin(t * Mathf.PI) * GateEntryArcHeight;
                tr.position = Vector3.Lerp(start, doorway, eased) + Vector3.up * arc;
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            if (go == null) return;
            tr.position = doorway;
            vfx?.PlayDashEnd(dir);
            completed = true;
        }
        catch (OperationCanceledException) { }
        finally
        {
            bool ownsState = state != null && state.Embodiment == go;
            if (!completed && go != null && ownsState)
            {
                vfx?.StopDash();
                action?.CompleteSectorGateTransition();
                state.CancelEmbodiedGateExit();

                if (action != null)
                    action.enabled = actionWas;
                if (brain != null)
                    brain.enabled = brainWas;
            }
        }

        // 통과 완료 → 매크로 인계(몸체 해제). 취소(플레이어가 섹터를 떠나 중도 파괴)면 인계하지 않는다.
        if (completed && state != null && state.Embodiment == go)
        {
            action?.CompleteSectorGateTransition();
            FinalizeGateExit(state, destination, gate);
        }
    }

    // 월드 좌표의 y를 해당 섹터 nav 그래프의 지면 높이로 스냅한다(잡몹의 map.ToWorld와 동일한 높이 출처).
    // 섹터 nav 블롭이 아직 없거나 XZ가 nav 영역 밖이면 원래 위치를 그대로 반환한다.
    private static Vector3 ResolveNavSafePosition(Sector sector, Vector3 position, float agentRadius)
    {
        MapNavigationAuthoring map = sector != null ? sector.NavAuthoring : null;
        if (map == null || !map.NavBlobData.IsCreated)
            return position;

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

        return resolved;
    }

    private static float ResolveAgentRadius(SO_Elite_Data data)
    {
        SO_Elite_Brain brain = data != null ? data.Brain : null;
        return brain != null ? Mathf.Max(0f, brain.AgentRadius) : 0.35f;
    }

    private static void ReleaseEmbodiment(Elite_State state, bool preservePendingExit = false, Sector observedSector = null)
    {
        if (state == null || state.Embodiment == null) return;

        Sector pendingExit = state.PendingExitSector;
        bool shouldPreserveExit = preservePendingExit
                                  && pendingExit != null
                                  && pendingExit != state.CurrentSector;
        bool wasExitDash = shouldPreserveExit && state.IsGateEntryAnimating;

        GameObject instance = state.Embodiment;
        Transform tr = instance != null ? instance.transform : null;
        if (tr != null)
        {
            state.WorldPosition = tr.position;
            state.Forward = tr.forward.sqrMagnitude > 0.0001f
                ? tr.forward.normalized
                : Vector3.forward;
        }

        state.CancelEmbodiedGateExit();
        state.DetachEmbodiment();
        CharacterSpawner.Release(instance);

        if (wasExitDash)
        {
            state.CancelFieldTravel();
            if (pendingExit == observedSector)
            {
                BeginObservedGateArrivalAfterRelease(state, pendingExit);
                return;
            }

            Elite_WorldSimulator.BeginMacroArrivalTravel(state, pendingExit);
            return;
        }

        if (shouldPreserveExit)
        {
            state.CancelFieldTravel();
            Elite_WorldSimulator.BeginMacroTravel(state, pendingExit);
            return;
        }

        if (preservePendingExit)
            state.FieldThinkTimer = 0f;
    }

    private static void BeginObservedGateArrivalAfterRelease(Elite_State state, Sector destination)
    {
        if (state == null || destination == null)
            return;

        Sector from = state.CurrentSector;
        Vector3 entryStart = state.WorldPosition;
        Vector3 arrival = Elite_WorldSimulator.ResolveGateExitArrival(from, destination, state);

        state.CurrentSector = destination;
        state.WorldPosition = ResolveNavSafePosition(destination, arrival, ResolveAgentRadius(state.Data));
        state.BeginGateArrival(from, entryStart);
        state.FieldThinkTimer = ResolveGateArrivalThinkDelay(state);
    }

    // 실체화 엘리트가 게이트까지 걸어와 통과 → 몸체를 해제하고 목적지 섹터의 매크로(비실체)로 인계한다.
    // 진입(매크로→게이트 대쉬→실체화)의 대칭 출구. Elite_Brain.TickGateExit가 게이트 도착 시 호출한다.
    private static bool TryBeginObservedGateArrival(Elite_State state, Sector observedSector)
    {
        if (state == null || observedSector == null || state.Embodiment != null)
            return false;
        if (state.CurrentSector == observedSector)
            return false;
        if (state.FieldDestinationSector != observedSector)
            return false;

        state.CancelFieldTravel();
        BeginObservedGateArrivalAfterRelease(state, observedSector);
        return true;
    }

    public void FinalizeGateExit(Elite_State state, Sector destination, SectorGate gate)
    {
        if (state == null || destination == null) return;

        // 도착 좌표 = 반대편(목적지) 게이트 스폰 지점.
        Vector3 arrival = Elite_WorldSimulator.ResolveGateExitArrival(state.CurrentSector, destination, state);

        ReleaseEmbodiment(state); // 몸체 파괴(+PendingExitSector 정리) → 비실체 복귀.

        state.CurrentSector = destination;
        state.WorldPosition = ResolveNavSafePosition(destination, arrival, ResolveAgentRadius(state.Data));

        Vector3 toCenter = destination.transform.position - state.WorldPosition;
        toCenter.y = 0f;
        if (toCenter.sqrMagnitude > 0.0001f)
            state.Forward = toCenter.normalized;

        state.FieldThinkTimer = ResolveGateArrivalThinkDelay(state);

        RefreshEmbodiments(); // 목적지가 (혹시) 플레이어 섹터면 재실체화, 아니면 마커만.
    }

    private static float ResolveGateArrivalThinkDelay(Elite_State state)
    {
        SO_Elite_Brain brain = state != null && state.Data != null ? state.Data.Brain : null;
        float cap = brain != null ? Mathf.Max(0.1f, brain.FieldThinkInterval) : GateArrivalThinkDelayMax;
        float seed = ((state != null ? state.Id : 0) + 1) * 17.271f + Time.time * 0.61f;
        float jitter = Mathf.Repeat(Mathf.Sin(seed) * 24634.6345f, 1f);
        return Mathf.Min(cap, Mathf.Lerp(GateArrivalThinkDelayMin, GateArrivalThinkDelayMax, jitter));
    }

    private void AddMinimapMarker(Elite_State state)
    {
        if (_minimap == null || state == null || _markers.ContainsKey(state)) return;

        SO_Elite_Data data = state.Data;
        Hud_GameScene_Minimap.Marker marker =
            _minimap.AddEliteMarker(state, data != null ? data.MarkerSprite : null);
        if (marker != null)
            _markers.Add(state, marker);
    }

    private void RemoveMinimapMarker(Elite_State state)
    {
        if (_minimap == null || state == null) return;
        if (!_markers.TryGetValue(state, out Hud_GameScene_Minimap.Marker marker)) return;

        _markers.Remove(state);
        _minimap.RemoveMarker(marker);
    }

    private void ClearMinimapMarkers()
    {
        if (_minimap != null)
        {
            foreach (Hud_GameScene_Minimap.Marker marker in _markers.Values)
                _minimap.RemoveMarker(marker);
        }

        _markers.Clear();
    }

    private void CancelPendingEmbodiments()
    {
        _embodyCts?.Cancel();
        _embodyCts?.Dispose();
        _embodyCts = null;
    }

    // 튜닝값은 SO_SectorBattle_Settings에서 주입(없으면 기본값 폴백).
    private float DeathDisplayDelay  => _settings != null ? _settings.DeathDisplayDelay  : 1.8f;
    private float EliteDamagePerHostilePowerPerSec =>
        _settings != null ? _settings.EliteDamagePerHostilePowerPerSec : 0.03f;

    // 비실체 엘리트는 같은 섹터의 상대 진영 Power에 비례해 피해를 받는다.
    // 자기 Power는 섹터 전투에만 기여하며 자신의 체력 피해를 상쇄하지 않는다.
    private void TickEliteAttrition(float deltaTime)
    {
        if (_sectorBattleManager == null || deltaTime <= 0f) return;

        Sector playerSector = _sectorManager != null ? _sectorManager.CurrentSector : null;

        for (int i = 0; i < _elites.Count; i++)
        {
            Elite_State e = _elites[i];
            if (e == null || !e.IsAlive) continue;
            if (e.Embodiment != null || e.CurrentSector == playerSector) continue; // 실체화/플레이어 섹터는 실제 전투가 처리.
            if (e.IsFieldTraveling) continue;                                       // 게이트 이동 중은 안전.
            if (!_sectorBattleManager.TryGetState(e.CurrentSector, out SectorBattleState s)) continue;

            float hostilePower = e.Faction == NavFaction.Ally ? s.EnemyPower : s.AllyPower;
            e.Health -= hostilePower * EliteDamagePerHostilePowerPerSec * deltaTime;
        }
    }

    // 죽은(Health<=0) 장수를 확실히 수거한다. 실체면 연출 시간만큼 두었다가 Unregister(→몸체 해제·마커 제거·_elites 제외).
    // 디스폰이 몸체 코루틴이 아니라 여기(매니저 틱)에 있어, 섹터 전환으로 몸체가 먼저 해제돼도 레코드가 누수되지 않는다.
    private void ReapDeadElites(float deltaTime)
    {
        for (int i = _elites.Count - 1; i >= 0; i--)   // 뒤에서부터 → Unregister(_elites 제거)가 안전.
        {
            Elite_State state = _elites[i];
            if (state == null) continue;

            if (state.IsAlive)
            {
                state.DeathElapsed = 0f;
                continue;
            }

            state.DeathElapsed += Mathf.Max(0f, deltaTime);
            float delay = state.Embodiment != null ? DeathDisplayDelay : 0f;
            if (state.DeathElapsed >= delay)
                Unregister(state);
        }
    }

    private async UniTaskVoid RunWorldSimulationAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, token);

                Sector current = _sectorManager != null ? _sectorManager.CurrentSector : null;

                // 장수 매크로 이동: 적대 잡몹이 있는 섹터는 점령 매니저에 물어본다(엘리트 백그라운드 DPS 전투는 폐지).
                Func<Sector, NavFaction, bool> hasBackgroundHostile =
                    _sectorBattleManager != null ? _sectorBattleManager.HasHostile : null;

                bool needsRefresh = _worldSimulator.Tick(
                    _elites,
                    current,
                    Time.deltaTime,
                    hasBackgroundHostile);

                TickEliteAttrition(Time.deltaTime);
                ReapDeadElites(Time.deltaTime);
                TickCapitalSiege();

                if (needsRefresh)
                    RefreshEmbodiments();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
