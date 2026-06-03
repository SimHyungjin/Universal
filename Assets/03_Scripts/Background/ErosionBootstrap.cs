using System.Collections.Generic;
using MapNav.Ecs;
using UnityEngine;

// 침식도로 시작 보드를 만든다: 적 본진(플레이어서 홉 최대거리 섹터)에서 게이트 BFS로 적색을 한 덩어리로 키우고,
// 플레이어 시작 섹터는 항상 아군으로 남긴다. 각 섹터의 시작 점령 진영을 산출한다.
//   · 적색 수 = clamp(ceil(stage/9 × 섹터수), 0, 섹터수-1)  ← 플레이어 섹터는 절대 적색이 안 됨.
//   · 적 엘리트는 EnemyHome(앵커) 한 섹터에서 전원 출발.
public sealed class ErosionBootstrap
{
    private readonly Dictionary<Sector, NavFaction> _owners = new();
    public IReadOnlyDictionary<Sector, NavFaction> Owners => _owners;
    public Sector EnemyHome { get; }
    public Sector PlayerStart { get; }

    public ErosionBootstrap(MinimapModel map, Sector playerStart, int erosionStage)
    {
        PlayerStart = playerStart;

        List<Sector> sectors = CollectSectors(map);
        if (sectors.Count == 0)
            return;

        EnemyHome = FindFarthestSector(playerStart, sectors);
        Sector anchor = EnemyHome != null ? EnemyHome : sectors[0];

        int n = sectors.Count;
        int redCount = Mathf.Clamp(Mathf.CeilToInt(erosionStage / 9f * n), 0, n - 1);

        HashSet<Sector> red = GrowRedSet(anchor, playerStart, redCount);
        for (int i = 0; i < sectors.Count; i++)
            _owners[sectors[i]] = red.Contains(sectors[i]) ? NavFaction.Enemy : NavFaction.Ally;
    }

    private static List<Sector> CollectSectors(MinimapModel map)
    {
        var list = new List<Sector>();
        if (map?.Nodes == null)
            return list;

        for (int i = 0; i < map.Nodes.Count; i++)
        {
            Sector s = map.Nodes[i]?.Sector;
            if (s != null && !list.Contains(s))
                list.Add(s);
        }
        return list;
    }

    // start에서 게이트 BFS로 가장 깊은(홉 최대) 섹터. BFS는 깊이 비감소 순으로 dequeue하므로 마지막이 최심부.
    private static Sector FindFarthestSector(Sector start, List<Sector> all)
    {
        if (start == null)
            return null;

        var visited = new HashSet<Sector> { start };
        var queue = new Queue<Sector>();
        queue.Enqueue(start);

        Sector farthest = start;
        var neighbors = new List<Sector>();
        while (queue.Count > 0)
        {
            Sector cur = queue.Dequeue();
            farthest = cur;

            neighbors.Clear();
            CollectNeighbors(cur, neighbors);
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (neighbors[i] == null || visited.Contains(neighbors[i])) continue;
                visited.Add(neighbors[i]);
                queue.Enqueue(neighbors[i]);
            }
        }

        return farthest == start ? null : farthest;
    }

    // anchor에서 BFS 순서로 redCount개를 채운다(playerStart는 적색에서 제외).
    private static HashSet<Sector> GrowRedSet(Sector anchor, Sector playerStart, int redCount)
    {
        var red = new HashSet<Sector>();
        if (anchor == null || redCount <= 0)
            return red;

        var visited = new HashSet<Sector> { anchor };
        var queue = new Queue<Sector>();
        queue.Enqueue(anchor);

        var neighbors = new List<Sector>();
        while (queue.Count > 0 && red.Count < redCount)
        {
            Sector cur = queue.Dequeue();
            if (cur != playerStart)
                red.Add(cur);

            neighbors.Clear();
            CollectNeighbors(cur, neighbors);
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (neighbors[i] == null || visited.Contains(neighbors[i])) continue;
                visited.Add(neighbors[i]);
                queue.Enqueue(neighbors[i]);
            }
        }

        return red;
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
}
