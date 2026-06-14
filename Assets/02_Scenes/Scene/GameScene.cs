using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MapNav.Ecs;
using UnityEngine;

public class GameScene : SceneBase
{
    // 캐릭터 선택 UI 전까지 사용할 기본 플레이어 캐릭터 데이터의 Addressables 주소.
    private const string DefaultPlayerDataKey = "SO_Player_Data";
    private Elite_Manager _eliteManager;
    private SectorBattleManager _sectorBattleManager;
    private PlayerController _playerController;
    private Character_Vitals _playerVitals;
    private bool _gameEnded; // 승패 확정 1회 가드(사망·전멸·승리 중 먼저 온 것).

    // 결과 배너 표시 후 자동 재시작까지의 시간(초). 우선 하드코딩.
    private const float ResultRestartDelay = 3f;

    public override async UniTask EnterScene(CancellationToken token)
    {
        await UniTask.Yield(cancellationToken: token);

        SectorManager sectorManager = await App.Instantiate<SectorManager>("SectorManager", token: token);
        var catalog = await App.LoadAssetAsync<SO_Sector_Catalog>("SO_Sector_Catalog", token: token);
        
        var generator = new SectorGenerator(
            sectorCount: 2,
            gridSize: new Vector2Int(8, 6),
            cellSize: 200f,
            extraConnectionCount: 2,
            catalog: catalog);
        await generator.GenerateAsync(token);

        // 점령(영토) 시뮬레이션이 잡몹 인구의 진실. 시작 섹터도 집계에 포함해 미니맵에 점령 상태가 뜨게 한다.
        // 튜닝 값은 SectorManager에 끼운 SO(SO_SectorBattle_Settings)에서 주입(없으면 코드 기본값).
        SO_SectorBattle_Settings battleSettings = sectorManager.BattleSettings;
        var startSettings = await App.LoadAssetAsync<SO_Secter_AliveSetting>(token: token);

        // 침식도로 시작 보드(섹터별 점령 진영) 산출. 플레이어 시작 섹터는 항상 아군, 적은 본진 한 섹터에서 출발.
        Sector playerStart = generator.StartSector;
        var erosion = new ErosionBootstrap(
            generator.Map, playerStart, startSettings != null ? startSettings.erosionStage : 0);

        int defaultCapacity = startSettings != null ? startSettings.defaultSectorCapacity : 0;
        Func<Sector, NavFaction?> ownerOf = s =>
            s != null && erosion.Owners.TryGetValue(s, out NavFaction o) ? o : (NavFaction?)null;
        Func<Sector, int> capacityOf = s =>
            s == null ? 0 : (s.Capacity > 0 ? s.Capacity : defaultCapacity);

        // 시작 점령은 침식 시드(진영×용량)로, 실체화 구성(무엇을)은 진영별 SO_Mob_Composition으로 주입.
        _sectorBattleManager = new SectorBattleManager(
            generator.Map, null, sectorManager, battleSettings,
            ownerOf, capacityOf,
            startSettings != null ? startSettings.allyComposition : null,
            startSettings != null ? startSettings.enemyComposition : null);

        // 플레이어가 자기 섹터를 100% 점령한 순간 배너로 알린다(승패 루프 UI 슬라이스).
        _sectorBattleManager.PlayerSectorCaptured += OnPlayerSectorCaptured;
        // 패배 조건 ②: 아군 점령 섹터 전멸.
        _sectorBattleManager.AllyEliminated += OnAllyEliminated;

        // 진입 시 점령 상태(진영 비율)대로 표시상한만큼 잡몹을 스폰한다.
        sectorManager.SetMobSpawnResolver(ResolveMobSpawnsFromBackground);

        // 침식 모드: 섹터별 자동 엘리트 시딩을 끄고, 아래 SeedStartRoster로만 영역 분산 배치한다.
        _eliteManager = new Elite_Manager(
            sectorManager, generator.Map, _sectorBattleManager, battleSettings);

        // 본진 결전: 플레이어가 적 본진(침식 앵커)에 진입하면 살아있는 적 엘리트가 전원 소집된다.
        // SeedStartRoster보다 먼저 호출 — 보스(Boss 역할)를 시작부터 본진에 직접 스폰하려면 capital이 확정돼 있어야 한다.
        _eliteManager.SetCapital(erosion.EnemyHome);

        // 시작 엘리트를 각 진영 영역 전체에 분산 배치(본진 1곳 집중 대신) → 시작부터 영역을 지키는 상태.
        // 이후 매크로 AI가 역할대로 정렬한다(Defender=허브, Vanguard=전선 강습 등). 보스만 본진에 고정 스폰.
        if (startSettings != null)
        {
            List<Sector> allySectors = CollectErosionSectors(erosion, NavFaction.Ally);
            List<Sector> enemySectors = CollectErosionSectors(erosion, NavFaction.Enemy);
            _eliteManager.SeedStartRoster(startSettings.allyElites, allySectors, NavFaction.Ally);
            _eliteManager.SeedStartRoster(startSettings.enemyElites, enemySectors, NavFaction.Enemy);
        }

        // 승리: 본진 결전에서 소집된 적 엘리트 전멸.
        _eliteManager.SiegeWon += OnSiegeWon;

        sectorManager.Enter(playerStart);

        // 스폰 위치 결정(연결된 게이트 우선, 없으면 섹터 중심 상공).
        SectorGate spawnGate = GetRandomConnectedGate(generator.StartSector.Gates);
        Vector3 spawnPosition = spawnGate != null
            ? spawnGate.SpawnPosition
            : generator.StartSector.transform.position + Vector3.up * 5f;

        // 데이터 기반 소환: 선택된 캐릭터 SO를 받아 그 프리팹(SO.Prefab) 참조를 실체화한다.
        // TODO: 캐릭터 선택 UI가 생기면 그 결과로 키/SO를 교체. 당장은 기본 캐릭터를 로드.
        var playerData = await App.LoadAssetAsync<SO_Character_Data>(DefaultPlayerDataKey, token: token);
        GameObject playerGo = await CharacterSpawner.SpawnPrefabAsync(
            playerData, new SpawnRequest(spawnPosition, Vector3.forward, generator.StartSector), token);

        if (playerGo == null)
        {
            Debug.LogError("[GameScene] 플레이어 소환에 실패했습니다.");
            return;
        }

        // '플레이어' = 자율(AI 기본) 캐릭터에 입력·카메라·HUD·진영(Ally)을 꽂는 빙의(possession).
        _playerController = new PlayerController();
        _playerController.Possess(playerGo, playerData);

        // 패배 조건 ①: 플레이어 사망.
        _playerVitals = playerGo.GetComponent<Character_Vitals>();
        if (_playerVitals != null)
            _playerVitals.OnDied += OnPlayerDied;

        Hud_GameScene hud = await App.ShowHud<Hud_GameScene>(token: token);
        if (hud != null)
        {
            hud.Bind(playerGo.GetComponent<Character_ActionHandler>());
            hud.BindMinimap(generator.Map, playerGo.transform, playerData);
            hud.BindEliteManager(_eliteManager);
            hud.BindCapital(erosion.EnemyHome); // 결전 목표(본진) 미니맵 마커.
        }
    }

    public override void ExitScene()
    {
        if (_playerVitals != null)
            _playerVitals.OnDied -= OnPlayerDied;
        _playerVitals = null;

        // 빙의 해제: 입력 맵·카메라 추종 정리(구 Player_Actor.OnDisable 역할).
        _playerController?.Release();
        _playerController = null;

        if (_sectorBattleManager != null)
        {
            _sectorBattleManager.PlayerSectorCaptured -= OnPlayerSectorCaptured;
            _sectorBattleManager.AllyEliminated -= OnAllyEliminated;
        }
        _sectorBattleManager?.Dispose();
        _sectorBattleManager = null;

        if (_eliteManager != null)
            _eliteManager.SiegeWon -= OnSiegeWon;
        _eliteManager?.Dispose();
        _eliteManager = null;
    }

    private NavAgentSpawnEntry[] ResolveMobSpawnsFromBackground(Sector sector)
        => _sectorBattleManager != null ? _sectorBattleManager.BuildEntrySpawns(sector) : null;

    // 플레이어가 현재 섹터를 100% 점령한 순간의 연출.
    // 폰트(Roboto)에 한글 글리프가 없어 배너는 당장 영어로 표기한다(한글 폰트/폴백 도입 전).
    private void OnPlayerSectorCaptured(Sector sector)
    {
        // 점령 순간 짧은 히트스톱으로 타격감을 준다 — 기존 전투 히트스톱 시스템 재사용(BannerMessage와 무관).
        // 배너는 unscaledTime으로 애니메이션하므로 월드가 멎는 동안에도 정상 재생된다.
        // 연출 값은 우선 하드코딩 — 추후 점령 연출 튜닝 SO로 옮길 수 있다.
        CombatOnHit.TriggerHitstop(
            new AttackHitstopData { duration = 0.5f, timeScale = 0.02f },
            CancellationToken.None).Forget();

        Popup_BannerMessage.ShowAsync("SECTOR CAPTURED!").Forget();
    }

    // ── 승패 처리 ──────────────────────────────────────────────────────────────────
    private void OnSiegeWon() => EndGame(true).Forget();
    private void OnAllyEliminated() => EndGame(false).Forget();
    private void OnPlayerDied() => EndGame(false).Forget();

    // 승패 확정 → 결과 배너를 띄우고 잠시 뒤 GameScene을 재로드해 한 판을 재시작한다.
    // 폰트(Roboto)에 한글 글리프가 없어 결과 문구도 당장 영어로 표기한다.
    private async UniTaskVoid EndGame(bool win)
    {
        if (_gameEnded) return;
        _gameEnded = true;

        Popup_BannerMessage.ShowAsync(win ? "VICTORY!" : "DEFEATED", ResultRestartDelay).Forget();

        // 결과 표시 동안 대기. 사망/히트스톱으로 timeScale이 낮아져 있어도 진행되도록 Realtime.
        await UniTask.Delay(TimeSpan.FromSeconds(ResultRestartDelay), DelayType.Realtime);

        // 재로드 중 Main.Clear가 timeScale·루프 이벤트를 리셋하므로 슬로모/히트스톱 잔여도 깨끗이 복원된다.
        Main.Scene.ReloadCurrentSceneAsync().Forget();
    }

    // 침식 보드에서 해당 진영이 점령한 섹터 목록(시작 엘리트를 영역 전체에 분산 배치하는 대상).
    private static List<Sector> CollectErosionSectors(ErosionBootstrap erosion, NavFaction faction)
    {
        var list = new List<Sector>();
        if (erosion?.Owners == null) return list;

        foreach (KeyValuePair<Sector, NavFaction> kv in erosion.Owners)
            if (kv.Value == faction) list.Add(kv.Key);
        return list;
    }

    private static SectorGate GetRandomConnectedGate(SectorGate[] gates)
    {
        if (gates == null || gates.Length == 0) return null;

        int connectedCount = 0;
        for (int i = 0; i < gates.Length; i++)
        {
            if (gates[i] != null && gates[i].IsConnected) connectedCount++;
        }

        if (connectedCount == 0) return null;

        int selected = UnityEngine.Random.Range(0, connectedCount);
        for (int i = 0; i < gates.Length; i++)
        {
            if (gates[i] == null || !gates[i].IsConnected) continue;
            if (selected-- == 0) return gates[i];
        }

        return null;
    }
}
