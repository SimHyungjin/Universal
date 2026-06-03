using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MapNav.Ecs;
using UnityEngine;

public class SectorManager : MonoBehaviour
{
    [SerializeField] private bool drawGateGizmos = true;
    [SerializeField] private float gateGizmoRadius = 2f;
    [SerializeField] private float gateLinkHeight = 3f;
    [SerializeField] private float gateLinkLineThickness = 3f;
    [SerializeField] private float gateDashArcHeight = 0.8f;

    [Header("Sector Battle Settings")]
    [Tooltip("섹터 점령/엘리트 튜닝 값(SO). 비우면 코드 기본값으로 동작.")]
    [SerializeField] private SO_SectorBattle_Settings battleSettings;
    public SO_SectorBattle_Settings BattleSettings => battleSettings;

    public static SectorManager Instance { get; private set; }

    public Sector CurrentSector { get; private set; }
    public bool IsTransitioning { get; private set; }

    /// <summary>현재 섹터가 바뀔 때 발생. ECS 잡몹은 SwitchMap으로 따라가지만,
    /// Mono 에이전트(장수 등)는 이 이벤트를 구독해 자기 nav 맵을 새 섹터 것으로 교체한다.</summary>
    public event Action<Sector> SectorChanged;

    private CancellationTokenSource _spawnCts;
    private Func<Sector, NavAgentSpawnEntry[]> _mobSpawnResolver;

    private void Awake()     => Instance = this;
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        _spawnCts?.Cancel();
        _spawnCts?.Dispose();
    }

    // 게임 시작 시 초기 진입 (대쉬 없음, 즉시 스폰)
    public void Enter(Sector sector)
    {
        if (sector == null || sector == CurrentSector) return;

        CurrentSector = sector;
        NavRuntimeBootstrap.Instance?.SwitchMap(sector.NavAuthoring);
        SectorChanged?.Invoke(sector);
        NavRuntimeBootstrap.Instance?.DrainAllAgents();
        ApplyFactionSeeds(sector);
        CharacterSpawner.SpawnMobs(GetMobSpawns(sector));
        PrewarmNeighborNavMaps(sector, destroyCancellationToken);
    }

    // 게이트 통과 시 전환 (대쉬 + 점진적 스폰 동시)
    public void Enter(SectorGate arrivalGate)
    {
        if (IsTransitioning) return;

        Sector targetSector = arrivalGate.Sector;
        if (targetSector == null || targetSector == CurrentSector) return;

        _spawnCts?.Cancel();
        _spawnCts?.Dispose();
        _spawnCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);

        CurrentSector = targetSector;
        NavRuntimeBootstrap.Instance?.SwitchMap(targetSector.NavAuthoring);
        SectorChanged?.Invoke(targetSector);
        NavRuntimeBootstrap.Instance?.DrainAllAgents();
        ApplyFactionSeeds(targetSector);

        TransitionAsync(arrivalGate, targetSector, _spawnCts.Token).Forget();
        PrewarmNeighborNavMaps(targetSector, _spawnCts.Token);
    }

    private async UniTaskVoid TransitionAsync(SectorGate arrivalGate, Sector sector, CancellationToken ct)
    {
        // 대쉬와 점진 스폰을 동시에 시작
        IsTransitioning = true;
        try
        {
            var spawnTask  = CharacterSpawner.SpawnMobsGraduallyAsync(GetMobSpawns(sector), ct: ct);

            var dashTask = DashPlayerTo(arrivalGate.SpawnPosition, ct);

            await UniTask.WhenAll(spawnTask, dashTask);
        }
        finally
        {
            arrivalGate.StartPairCooldown();
            IsTransitioning = false;
        }
    }

    private async UniTask DashPlayerTo(Vector3 destination, CancellationToken ct)
    {
        Player_Actor player = FindAnyObjectByType<Player_Actor>();
        if (player == null) return;

        Transform playerTransform = player.transform;
        Vector3 start = playerTransform.position;
        Vector3 planarDirection = destination - start;
        planarDirection.y = 0f;
        if (planarDirection.sqrMagnitude <= 0.0001f)
            planarDirection = playerTransform.forward;
        planarDirection.Normalize();

        Character_MoveController moveController = player.GetComponent<Character_MoveController>();
        Character_ActionHandler actionHandler = player.GetComponent<Character_ActionHandler>();
        Character_Vfx vfx = player.GetComponent<Character_Vfx>();
        bool restoreActionHandler = actionHandler != null && actionHandler.enabled;

        actionHandler?.PrepareSectorGateTransition();

        if (actionHandler != null)
            actionHandler.enabled = false;
        moveController?.StopPlanar();
        moveController?.StopLunge();

        float gateTransitionSpeed = actionHandler != null ? actionHandler.GateTransitionSpeed : 18f;
        float duration = ResolveDashDuration(start, destination, gateTransitionSpeed);
        playerTransform.rotation = Quaternion.LookRotation(planarDirection, Vector3.up);
        App.AlignThirdPersonCameraToTargetYaw(duration);
        vfx?.PlayDashStart(planarDirection);

        float elapsed = 0f;
        bool completed = false;

        try
        {
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);
                float arc = Mathf.Sin(t * Mathf.PI) * gateDashArcHeight;
                playerTransform.position = Vector3.Lerp(start, destination, eased) + Vector3.up * arc;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            playerTransform.position = destination;
            vfx?.PlayDashEnd(planarDirection);
            actionHandler?.CompleteSectorGateTransition();
            completed = true;
        }
        finally
        {
            if (!completed)
                vfx?.StopDash();

            if (actionHandler != null && restoreActionHandler)
                actionHandler.enabled = true;
        }
    }

    private void PrewarmNeighborNavMaps(Sector sector, CancellationToken ct)
    {
        PrewarmNeighborNavMapsAsync(sector, ct).Forget();
    }

    private static async UniTaskVoid PrewarmNeighborNavMapsAsync(Sector sector, CancellationToken ct)
    {
        if (sector == null || NavRuntimeBootstrap.Instance == null)
            return;

        try
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);

            SectorGate[] gates = sector.Gates;
            if (gates == null)
                return;

            for (int i = 0; i < gates.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                SectorGate connected = gates[i] != null ? gates[i].ConnectedGate : null;
                Sector neighbor = connected != null ? connected.Sector : null;
                MapNavigationAuthoring map = neighbor != null ? neighbor.NavAuthoring : null;
                NavRuntimeBootstrap.Instance?.PrewarmMap(map);

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static float ResolveDashDuration(Vector3 start, Vector3 destination, float gateTransitionSpeed)
    {
        Vector3 planarDelta = destination - start;
        planarDelta.y = 0f;

        float distance = planarDelta.magnitude;
        float speed = Mathf.Max(0.01f, gateTransitionSpeed);
        return Mathf.Max(0.01f, distance / speed);
    }

    // 게이트마다 인접 섹터의 점령 진영을 읽어 잡몹 스폰 시드를 만든다(점령 상태가 전선/증원 방향에 반영).
    // 적 점령 인접 섹터 쪽 게이트 = 적 증원, 아군 점령 쪽 = 아군 증원. 플레이어가 막 들어온 게이트는 보통 아군 쪽.
    private void ApplyFactionSeeds(Sector sector)
    {
        var ally = new List<Vector3>();
        var enemy = new List<Vector3>();
        SectorBattleManager battle = SectorBattleManager.Instance;
        SectorGate[] gates = sector != null ? sector.Gates : null;

        if (gates != null)
        {
            for (int i = 0; i < gates.Length; i++)
            {
                SectorGate gate = gates[i];
                if (gate == null || gate.ConnectedGate == null) continue;

                NavFaction owner = NavFaction.Enemy; // 미등록/미점령 인접은 적지로 간주.
                if (battle != null && battle.TryGetState(gate.ConnectedGate.Sector, out SectorBattleState s))
                    owner = s.OwnerFaction;

                if (owner == NavFaction.Ally) ally.Add(gate.SpawnPosition);
                else enemy.Add(gate.SpawnPosition);
            }
        }

        NavRuntimeBootstrap.Instance?.SetFactionSeeds(ally, enemy);
    }

    public NavAgentSpawnEntry[] GetMobSpawns(Sector sector)
    {
        // 진입 스폰은 resolver(점령 상태 → 표시상한)를 우선한다. override는 초기 Reserve 구성에만 쓴다.
        NavAgentSpawnEntry[] resolved = _mobSpawnResolver?.Invoke(sector);
        if (resolved != null)
            return resolved;

        return null;
    }

    // 초기 점령 집계용. 섹터별 레거시 스폰 구성은 제거되어 현재는 시작 설정의 owner/capacity 시드를 사용한다.
    public NavAgentSpawnEntry[] GetConfiguredMobSpawns(Sector sector)
        => null;

    public void SetMobSpawnResolver(Func<Sector, NavAgentSpawnEntry[]> resolver)
        => _mobSpawnResolver = resolver;

    private void OnDrawGizmos()
    {
        if (!drawGateGizmos) return;

        DrawCurrentSectorGizmo();
        DrawGateLinkGizmos();
    }

    private void DrawCurrentSectorGizmo()
    {
        if (CurrentSector == null) return;

        Gizmos.color = new Color(1f, 0.78f, 0.18f, 0.95f);
        Gizmos.DrawWireCube(CurrentSector.transform.position + Vector3.up * 2f, new Vector3(36f, 4f, 36f));
    }

    private void DrawGateLinkGizmos()
    {
        SectorGate[] gates = FindObjectsByType<SectorGate>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.45f);
        foreach (SectorGate gate in gates)
        {
            if (gate == null || gate.IsConnected) continue;
            Gizmos.DrawWireSphere(gate.transform.position + Vector3.up * gateLinkHeight, gateGizmoRadius);
        }

        foreach (SectorGate gate in gates)
        {
            SectorGate connectedGate = gate != null ? gate.ConnectedGate : null;
            if (connectedGate == null) continue;
            if (gate.GetInstanceID() > connectedGate.GetInstanceID()) continue;

            DrawGatePairGizmo(gate, connectedGate);
        }
    }

    private void DrawGatePairGizmo(SectorGate from, SectorGate to)
    {
        Vector3 fromPosition = from.transform.position + Vector3.up * gateLinkHeight;
        Vector3 toPosition   = to.transform.position + Vector3.up * gateLinkHeight;

        Color linkColor = new Color(0.1f, 0.85f, 1f, 0.95f);
        DrawThickLine(fromPosition, toPosition, linkColor);
        Gizmos.color = linkColor;
        Gizmos.DrawSphere(fromPosition, gateGizmoRadius);
        Gizmos.DrawSphere(toPosition, gateGizmoRadius);

        Color spawnColor = new Color(0.45f, 1f, 0.35f, 0.8f);
        DrawThickLine(fromPosition, from.SpawnPosition + Vector3.up, spawnColor);
        DrawThickLine(toPosition, to.SpawnPosition + Vector3.up, spawnColor);
        Gizmos.color = spawnColor;
        Gizmos.DrawWireSphere(from.SpawnPosition + Vector3.up, gateGizmoRadius * 0.6f);
        Gizmos.DrawWireSphere(to.SpawnPosition + Vector3.up, gateGizmoRadius * 0.6f);
    }

    private void DrawThickLine(Vector3 a, Vector3 b, Color color)
    {
#if UNITY_EDITOR
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.DrawLine(a, b, gateLinkLineThickness);
#else
        Gizmos.color = color;
        Gizmos.DrawLine(a, b);
#endif
    }
}
