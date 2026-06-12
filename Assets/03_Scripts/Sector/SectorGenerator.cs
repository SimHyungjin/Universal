using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class SectorGenerator
{
    private readonly int _sectorCount;
    private readonly Vector2Int _gridSize;
    private readonly float _cellSize;
    private readonly int _extraConnectionCount;
    private readonly SO_Sector_Catalog _catalog;

    public Sector StartSector { get; private set; }
    public MinimapModel Map { get; private set; }

    public SectorGenerator(
        int sectorCount,
        Vector2Int gridSize,
        float cellSize,
        int extraConnectionCount = 2,
        SO_Sector_Catalog catalog = null)
    {
        _sectorCount          = sectorCount;
        _gridSize             = gridSize;
        _cellSize             = cellSize;
        _extraConnectionCount = Mathf.Max(0, extraConnectionCount);
        _catalog              = catalog;
    }

    public async UniTask GenerateAsync(CancellationToken ct)
    {
        // 1) by-construction 성장: 연결 가능한 칸에만 섹터를 붙여 트리를 키운다.
        //    붙는 순간 스패닝 변을 예약하므로 전체가 항상 한 덩어리로 연결된다(조각남 X).
        List<Sector>         sectors      = new();
        List<Vector2Int>     positions    = new();
        List<int>            baseMask     = new(); // base 방향(회전 0) 기준 게이트 방향 집합
        List<int>            requiredMask = new(); // 물리적으로 향해야 하는 방향의 누적 집합
        List<(int a, int b)> edges        = new();
        Dictionary<Vector2Int, int> occupied = new();

        await GrowConnectedMap(sectors, positions, baseMask, requiredMask, edges, occupied, ct);

        int n = sectors.Count;
        if (n == 0) return;
        StartSector = sectors[0];

        int[] baseMaskArr     = baseMask.ToArray();
        int[] requiredMaskArr = requiredMask.ToArray();

        HashSet<(int a, int b)> connectedPairs = new();
        foreach (var (a, b) in edges)
            connectedPairs.Add(NormalizePair(a, b));

        // 2) 여분 연결(loop) — 회전으로 실현 가능할 때만
        int extraConnections = 0;
        foreach (var (a, b) in BuildExtraConnectionCandidates(positions, connectedPairs))
        {
            if (extraConnections >= _extraConnectionCount) break;
            if (!TryReserveEdge(a, b, positions, baseMaskArr, requiredMaskArr)) continue;

            edges.Add((a, b));
            connectedPairs.Add(NormalizePair(a, b));
            extraConnections++;
        }

        // 3) 누적 필요 방향을 덮는 회전을 섹터별로 골라 적용
        int[] rotation = new int[n];
        for (int i = 0; i < n; i++)
        {
            rotation[i] = FindCoveringRotation(baseMaskArr[i], requiredMaskArr[i]);
            if (rotation[i] < 0)
            {
                Debug.LogWarning($"[SectorGenerator] No rotation covers required gates. Sector={sectors[i].name}");
                rotation[i] = 0;
            }
            sectors[i].transform.rotation = Quaternion.Euler(0f, 90f * rotation[i], 0f);
        }

        // 4) 마주보는 물리 방향 게이트끼리 연결
        foreach (var (a, b) in edges)
        {
            if (!ConnectSectors(sectors[a], rotation[a], positions[a], sectors[b], rotation[b], positions[b]))
                Debug.LogWarning($"[SectorGenerator] Required sector link failed. A={sectors[a].name}, B={sectors[b].name}");
        }

        foreach (Sector s in sectors) s.FinalizeGates();

        // 5) 미니맵 모델 굽기 — 배치/회전/엣지 확정 상태를 그대로 정수 셀로 직렬화
        Map = BuildMinimapModel(sectors, positions, rotation, edges);
    }

    // ── by-construction 성장 ───────────────────────────────────────────────────
    // 시작 섹터에서 출발해, "연결 가능한(양쪽이 한 회전으로 필요 방향을 덮을 수 있는)" 빈 인접 칸에만
    // 새 섹터를 붙여 나간다. 붙는 순간 스패닝 변을 예약하므로 전체가 항상 한 덩어리로 연결된다.

    private async UniTask GrowConnectedMap(
        List<Sector> sectors,
        List<Vector2Int> positions,
        List<int> baseMask,
        List<int> requiredMask,
        List<(int a, int b)> edges,
        Dictionary<Vector2Int, int> occupied,
        CancellationToken ct)
    {
        int cellCapacity = Mathf.Max(0, _gridSize.x * _gridSize.y);
        int target       = Mathf.Min(_sectorCount, cellCapacity);
        if (target <= 0) return;

        // 시작 섹터
        Sector first = await InstantiateSector(ct);
        Vector2Int start = new(Random.Range(0, _gridSize.x), Random.Range(0, _gridSize.y));
        first.transform.position = GridToWorld(start);
        PlaceSector(first, start, BuildGateMask(first), sectors, positions, baseMask, requiredMask, occupied);

        int failures     = 0;
        int failureLimit = Mathf.Max(8, target * 2);

        while (sectors.Count < target && occupied.Count < cellCapacity)
        {
            Sector next  = await InstantiateSector(ct);
            int nextMask = BuildGateMask(next);

            if (TryPickPlacement(nextMask, positions, baseMask, requiredMask, occupied,
                                 out Vector2Int cell, out int parent, out GateDirection dir))
            {
                next.transform.position = GridToWorld(cell);
                int idx = PlaceSector(next, cell, nextMask, sectors, positions, baseMask, requiredMask, occupied);

                requiredMask[parent] |= Bit(dir);
                requiredMask[idx]    |= Bit(Opposite(dir));
                edges.Add((parent, idx));
                failures = 0;
            }
            else
            {
                // 이 프리팹은 현재 어떤 프런티어에도 못 붙음 → 폐기하고 다른 변종으로 재시도
                Object.Destroy(next.gameObject);
                if (++failures >= failureLimit) break;
            }
        }

        if (sectors.Count < target)
            Debug.LogWarning($"[SectorGenerator] Growth stopped early: {sectors.Count}/{target} sectors placed. " +
                             "게이트 구성이 부족하거나 그리드가 막혔습니다.");
    }

    private static int PlaceSector(
        Sector sector, Vector2Int pos, int mask,
        List<Sector> sectors, List<Vector2Int> positions, List<int> baseMask,
        List<int> requiredMask, Dictionary<Vector2Int, int> occupied)
    {
        int idx = sectors.Count;
        sectors.Add(sector);
        positions.Add(pos);
        baseMask.Add(mask);
        requiredMask.Add(0);
        occupied[pos] = idx;
        return idx;
    }

    // 새 섹터(nextMask)를 붙일 수 있는 프런티어 후보 중 하나를 무작위로 고른다.
    // 후보 = (빈 인접 칸 cell, 거기 닿은 기존 섹터 parent, parent→cell 방향 dir).
    // 실현 가능: parent가 requiredMask|dir을, next가 opposite(dir)을 각각 한 회전으로 덮을 수 있어야 함.
    private bool TryPickPlacement(
        int nextMask,
        List<Vector2Int> positions, List<int> baseMask, List<int> requiredMask,
        Dictionary<Vector2Int, int> occupied,
        out Vector2Int cell, out int parent, out GateDirection dir)
    {
        List<(Vector2Int cell, int parent, GateDirection dir)> candidates = new();

        for (int i = 0; i < positions.Count; i++)
        {
            for (int k = 0; k < 4; k++)
            {
                GateDirection d = DirFromCw(k);
                Vector2Int    c = positions[i] + Offset(d);

                if (c.x < 0 || c.y < 0 || c.x >= _gridSize.x || c.y >= _gridSize.y) continue;
                if (occupied.ContainsKey(c)) continue;
                if (FindCoveringRotation(baseMask[i], requiredMask[i] | Bit(d)) < 0) continue;
                if (FindCoveringRotation(nextMask, Bit(Opposite(d))) < 0) continue;

                candidates.Add((c, i, d));
            }
        }

        if (candidates.Count == 0)
        {
            cell = default; parent = -1; dir = default;
            return false;
        }

        var pick = candidates[Random.Range(0, candidates.Count)];
        cell = pick.cell; parent = pick.parent; dir = pick.dir;
        return true;
    }

    private static Vector2Int Offset(GateDirection dir) => dir switch
    {
        GateDirection.North => new Vector2Int(0, 1),
        GateDirection.East  => new Vector2Int(1, 0),
        GateDirection.South => new Vector2Int(0, -1),
        GateDirection.West  => new Vector2Int(-1, 0),
        _                   => default
    };

    // ── 미니맵 모델 ────────────────────────────────────────────────────────────
    // 3D 배치에 쓴 positions/rotation을 그대로 재사용하므로 좌표 동기화가 보장된다.

    private MinimapModel BuildMinimapModel(
        IReadOnlyList<Sector> sectors,
        List<Vector2Int> positions,
        int[] rotation,
        List<(int a, int b)> edges)
    {
        var model = new MinimapModel { GridSize = _gridSize, CellSize = _cellSize };

        for (int i = 0; i < sectors.Count; i++)
        {
            IReadOnlyList<Vector2Int> local = sectors[i].MinimapFootprint;
            var cells = new Vector2Int[local.Count];
            for (int k = 0; k < local.Count; k++)
                cells[k] = positions[i] + RotateCellCw(local[k], rotation[i]);

            ResolveRoomLocalBounds(sectors[i], out Vector2 worldSize, out Vector2 localCenter);

            model.Nodes.Add(new MinimapModel.Node
            {
                Index         = i,
                Sector        = sectors[i],
                AnchorCell    = positions[i],
                Cells         = cells,
                IsStart       = i == 0,
                Sprite        = sectors[i].MinimapSprite,
                FrameSprite   = sectors[i].MinimapFrameSprite,
                RotationSteps = rotation[i],
                WorldSize     = worldSize,
                LocalCenter   = localCenter,
            });
        }

        foreach (var (a, b) in edges)
            model.Edges.Add(new MinimapModel.Edge { A = a, B = b });

        return model;
    }

    // 섹터의 nav 영역 합집합 로컬 바운드(회전 전 x,z 평면). 미니맵 방을 실제 크기로 그려
    // 플레이어 마커와 동기화하는 데 쓴다. 영역이 없으면 size=0 → 렌더러가 기본 박스로 폴백.
    private static void ResolveRoomLocalBounds(Sector sector, out Vector2 size, out Vector2 center)
    {
        size = Vector2.zero;
        center = Vector2.zero;

        MapNavigationAuthoring nav = sector != null ? sector.NavAuthoring : null;
        if (nav == null) return;

        bool any = false;
        Vector2 min = new(float.MaxValue, float.MaxValue);
        Vector2 max = new(float.MinValue, float.MinValue);

        IReadOnlyList<MapNavRegion> regions = nav.Regions;
        for (int i = 0; i < regions.Count; i++)
        {
            MapNavRegion r = regions[i];
            if (r == null || r.Shapes == null || r.Shapes.Count == 0) continue;
            if (!r.HasBounds) r.RecalculateBounds();
            if (!r.HasBounds) continue;

            min = any ? Vector2.Min(min, r.BoundsMin) : r.BoundsMin;
            max = any ? Vector2.Max(max, r.BoundsMax) : r.BoundsMax;
            any = true;
        }

        if (!any) return;
        size   = max - min;
        center = (min + max) * 0.5f;
    }

    // 게이트 마스크 회전(RotateMaskCw)과 동일한 시계방향 컨벤션:
    // +1 스텝마다 N(0,1)→E(1,0)→S(0,-1)→W(-1,0). 정수라 오차 0.
    private static Vector2Int RotateCellCw(Vector2Int cell, int steps)
    {
        steps &= 3;
        for (int i = 0; i < steps; i++)
            cell = new Vector2Int(cell.y, -cell.x);
        return cell;
    }

    // ── 섹터 인스턴스화 ────────────────────────────────────────────────────────
    // 카탈로그가 있으면 가중 랜덤으로 변종을 뽑고, 없으면 기존 "Sector" 키로 폴백.

    private async UniTask<Sector> InstantiateSector(CancellationToken ct)
    {
        Sector prefab = _catalog != null ? _catalog.PickWeighted() : null;
        if (prefab != null) return Object.Instantiate(prefab);

        return await App.Instantiate<Sector>("Sector", token: ct);
    }

    // ── 연결 헬퍼 ────────────────────────────────────────────────────────────

    private static bool ConnectSectors(Sector a, int rotA, Vector2Int pa, Sector b, int rotB, Vector2Int pb)
    {
        GateDirection dirAtoB = DirectionTo(pa, pb);
        SectorGate gateA = GetPhysicalGate(a, rotA, dirAtoB);
        SectorGate gateB = GetPhysicalGate(b, rotB, Opposite(dirAtoB));
        return Link(gateA, gateB);
    }

    // 회전 rot이 적용된 섹터에서 '물리적으로' physicalDir을 향하는 게이트를 찾는다.
    // 회전 rot만큼 base 방향이 시계방향으로 돌므로, 필요한 base 방향은 physicalDir을 rot만큼 되돌린 것.
    private static SectorGate GetPhysicalGate(Sector sector, int rot, GateDirection physicalDir)
    {
        GateDirection baseDir = DirFromCw((CwIndex(physicalDir) - rot) & 3);
        return sector.GetGate(baseDir);
    }

    private static bool Link(SectorGate gateA, SectorGate gateB)
    {
        if (!CanLink(gateA, gateB))
        {
            Debug.LogWarning($"[SectorGenerator] Gate link failed. A={DescribeGate(gateA)}, B={DescribeGate(gateB)}");
            return false;
        }

        gateA.Connect(gateB);
        gateB.Connect(gateA);
        return true;
    }

    private static bool CanLink(SectorGate gateA, SectorGate gateB)
    {
        return gateA != null && gateB != null && !gateA.IsConnected && !gateB.IsConnected;
    }

    private static string DescribeGate(SectorGate gate)
    {
        if (gate == null) return "null";
        Sector sector = gate.Sector;
        string sectorName = sector != null ? sector.name : "no-sector";
        return $"{sectorName}/{gate.name}({gate.Direction})";
    }

    // ── 게이트 방향 마스크 / 회전 ──────────────────────────────────────────────
    // 비트 인덱스는 시계방향 순서(N=0, E=1, S=2, W=3). +90° 회전 = 인덱스 +1.

    private static int BuildGateMask(Sector sector)
    {
        int mask = 0;
        if (sector.Gates != null)
            foreach (SectorGate gate in sector.Gates)
                if (gate != null) mask |= Bit(gate.Direction);
        return mask;
    }

    // requiredMask를 덮는(rotatedBase ⊇ required) 최소 회전을 반환. 없으면 -1.
    private static int FindCoveringRotation(int baseMask, int requiredMask)
    {
        if (requiredMask == 0) return 0;
        for (int r = 0; r < 4; r++)
            if ((RotateMaskCw(baseMask, r) & requiredMask) == requiredMask)
                return r;
        return -1;
    }

    private static int RotateMaskCw(int mask, int steps)
    {
        steps &= 3;
        int result = 0;
        for (int i = 0; i < 4; i++)
            if ((mask & (1 << i)) != 0)
                result |= 1 << ((i + steps) & 3);
        return result;
    }

    private static int Bit(GateDirection dir) => 1 << CwIndex(dir);

    private static int CwIndex(GateDirection dir) => dir switch
    {
        GateDirection.North => 0,
        GateDirection.East  => 1,
        GateDirection.South => 2,
        GateDirection.West  => 3,
        _                   => 0
    };

    private static GateDirection DirFromCw(int index) => (index & 3) switch
    {
        0 => GateDirection.North,
        1 => GateDirection.East,
        2 => GateDirection.South,
        _ => GateDirection.West
    };

    private static GateDirection DirectionTo(Vector2Int from, Vector2Int to)
    {
        Vector2Int delta = to - from;
        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            return delta.x >= 0 ? GateDirection.East : GateDirection.West;
        return delta.y >= 0 ? GateDirection.North : GateDirection.South;
    }

    private static GateDirection Opposite(GateDirection dir) => dir switch
    {
        GateDirection.North => GateDirection.South,
        GateDirection.South => GateDirection.North,
        GateDirection.East  => GateDirection.West,
        GateDirection.West  => GateDirection.East,
        _                   => dir
    };

    // 변 (u,v)를 추가했을 때 양쪽이 회전으로 필요 방향을 덮을 수 있는지.
    private static bool IsEdgeFeasible(int u, int v, List<Vector2Int> positions, int[] baseMask, int[] requiredMask)
    {
        GateDirection dirUtoV = DirectionTo(positions[u], positions[v]);
        int newU = requiredMask[u] | Bit(dirUtoV);
        int newV = requiredMask[v] | Bit(Opposite(dirUtoV));
        return FindCoveringRotation(baseMask[u], newU) >= 0
            && FindCoveringRotation(baseMask[v], newV) >= 0;
    }

    // 실현 가능하면 requiredMask를 갱신하고 true. 아니면 변화 없이 false.
    private static bool TryReserveEdge(int a, int b, List<Vector2Int> positions, int[] baseMask, int[] requiredMask)
    {
        if (!IsEdgeFeasible(a, b, positions, baseMask, requiredMask)) return false;

        GateDirection dirAtoB = DirectionTo(positions[a], positions[b]);
        requiredMask[a] |= Bit(dirAtoB);
        requiredMask[b] |= Bit(Opposite(dirAtoB));
        return true;
    }

    // ── 배치 ─────────────────────────────────────────────────────────────────

    private static List<(int a, int b)> BuildExtraConnectionCandidates(
        List<Vector2Int> positions,
        HashSet<(int a, int b)> existingPairs)
    {
        List<(int a, int b, float distance)> candidates = new();

        for (int a = 0; a < positions.Count; a++)
        {
            for (int b = a + 1; b < positions.Count; b++)
            {
                if (existingPairs.Contains((a, b)) || !AreCardinalNeighbors(positions[a], positions[b])) continue;
                candidates.Add((a, b, Vector2.Distance(positions[a], positions[b])));
            }
        }

        candidates.Sort((left, right) => left.distance.CompareTo(right.distance));

        List<(int a, int b)> result = new(candidates.Count);
        foreach (var candidate in candidates)
            result.Add((candidate.a, candidate.b));

        return result;
    }

    private static (int a, int b) NormalizePair(int a, int b)
        => a < b ? (a, b) : (b, a);

    private static bool AreCardinalNeighbors(Vector2Int a, Vector2Int b)
    {
        Vector2Int delta = b - a;
        return Mathf.Abs(delta.x) + Mathf.Abs(delta.y) == 1;
    }

    private Vector3 GridToWorld(Vector2Int gridPos)
        => new Vector3(gridPos.x * _cellSize, 0f, gridPos.y * _cellSize);
}
