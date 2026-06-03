using System;
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

    public override async UniTask EnterScene(CancellationToken token)
    {
        await UniTask.Yield(cancellationToken: token);

        SectorManager sectorManager = await App.Instantiate<SectorManager>("SectorManager", token: token);
        var catalog = await App.LoadAssetAsync<SO_Sector_Catalog>("SO_Sector_Catalog", token: token);
        
        var generator = new SectorGenerator(
            sectorCount: 10,
            gridSize: new Vector2Int(5, 7),
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

        // 진입 시 점령 상태(진영 비율)대로 표시상한만큼 잡몹을 스폰한다.
        sectorManager.SetMobSpawnResolver(ResolveMobSpawnsFromBackground);

        // 침식 모드: 섹터별 자동 엘리트 시딩을 끄고, 아래 SeedStartRoster로만 중앙 배치한다.
        _eliteManager = new Elite_Manager(
            sectorManager, generator.Map, _sectorBattleManager, battleSettings);

        // 시작 엘리트 중앙 배치: 아군→플레이어 섹터, 적→본진(침식 앵커).
        if (startSettings != null)
        {
            _eliteManager.SeedStartRoster(startSettings.allyElites, playerStart, NavFaction.Ally);
            _eliteManager.SeedStartRoster(startSettings.enemyElites, erosion.EnemyHome, NavFaction.Enemy);
        }

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

        Player_Actor player = playerGo != null ? playerGo.GetComponent<Player_Actor>() : null;
        if (player == null)
        {
            Debug.LogError("[GameScene] 플레이어 소환에 실패했습니다.");
            return;
        }

        Hud_GameScene hud = await App.ShowHud<Hud_GameScene>(token: token);
        if (hud != null)
        {
            hud.Bind(player.GetComponent<Character_ActionHandler>());
            hud.BindMinimap(generator.Map, player.transform, playerData);
            hud.BindEliteManager(_eliteManager);
        }
    }

    public override void ExitScene()
    {
        _sectorBattleManager?.Dispose();
        _sectorBattleManager = null;

        _eliteManager?.Dispose();
        _eliteManager = null;
    }

    private NavAgentSpawnEntry[] ResolveMobSpawnsFromBackground(Sector sector)
        => _sectorBattleManager != null ? _sectorBattleManager.BuildEntrySpawns(sector) : null;

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
