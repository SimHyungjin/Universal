using MapNav.Data;
using Unity.Collections;
using Unity.Mathematics;

namespace MapNav.Core
{
    // 거시 경로(상위 A* 결과 = 노드 + 포털 시퀀스) 캐시.
    // 키 = (시작 NodeKey, 끝 NodeKey). 추격처럼 같은 리전 쌍을 반복 질의할 때 NavGraph.TryFindPath
    // (가장 비싼 단계 — 엣지마다 in-region 가시그래프 cost 측정)를 통째로 건너뛴다.
    // 포털은 맵 transform에만 의존(LiftPortal)하므로 start/end의 실제 좌표가 달라도 재사용 가능 —
    // 리전 내부 진입점 보정은 funnel/refine이 매번 정확히 수행하므로 경로 정밀도는 유지된다.
    //
    // 단일 스레드(NavPathBuildSystem의 메인스레드 foreach)에서만 접근한다. 풀은 append-only이며
    // 정적 맵에서 리전 쌍 수만큼 bounded — 맵이 바뀌면 Clear로 통째 비운다.
    public struct NavMacroPathCache : System.IDisposable
    {
        private struct Slot
        {
            public int NodeStart;
            public int NodeCount;
            public int PortalStart;
            public int PortalCount;
        }

        private NativeHashMap<int2, Slot> _map;
        private NativeList<NavSpaceRef> _nodes;
        private NativeList<NavPortal> _portals;

        public bool IsCreated => _map.IsCreated;

        public NavMacroPathCache(int capacity, Allocator allocator)
        {
            _map = new NativeHashMap<int2, Slot>(capacity, allocator);
            _nodes = new NativeList<NavSpaceRef>(capacity * 4, allocator);
            _portals = new NativeList<NavPortal>(capacity * 4, allocator);
        }

        private static int2 KeyOf(NavSpaceRef start, NavSpaceRef end)
            => new int2(NavGraph.NodeKey(start), NavGraph.NodeKey(end));

        public bool TryGet(
            NavSpaceRef start,
            NavSpaceRef end,
            ref NativeList<NavSpaceRef> outNodes,
            ref NativeList<NavPortal> outPortals)
        {
            if (!_map.IsCreated || !_map.TryGetValue(KeyOf(start, end), out Slot slot))
                return false;

            outNodes.Clear();
            for (int i = 0; i < slot.NodeCount; i++)
                outNodes.Add(_nodes[slot.NodeStart + i]);

            outPortals.Clear();
            for (int i = 0; i < slot.PortalCount; i++)
                outPortals.Add(_portals[slot.PortalStart + i]);

            return true;
        }

        public void Store(
            NavSpaceRef start,
            NavSpaceRef end,
            in NativeList<NavSpaceRef> nodes,
            in NativeList<NavPortal> portals)
        {
            if (!_map.IsCreated)
                return;

            int2 key = KeyOf(start, end);
            // 정적 맵에서 같은 리전 쌍은 결과가 동일하므로 한 번만 저장한다(풀 중복 누적 방지).
            if (_map.ContainsKey(key))
                return;

            Slot slot = new Slot
            {
                NodeStart = _nodes.Length,
                NodeCount = nodes.Length,
                PortalStart = _portals.Length,
                PortalCount = portals.Length
            };

            for (int i = 0; i < nodes.Length; i++)
                _nodes.Add(nodes[i]);
            for (int i = 0; i < portals.Length; i++)
                _portals.Add(portals[i]);

            _map.Add(key, slot);
        }

        public void Clear()
        {
            if (!_map.IsCreated)
                return;

            _map.Clear();
            _nodes.Clear();
            _portals.Clear();
        }

        public void Dispose()
        {
            if (_map.IsCreated) _map.Dispose();
            if (_nodes.IsCreated) _nodes.Dispose();
            if (_portals.IsCreated) _portals.Dispose();
        }
    }
}
