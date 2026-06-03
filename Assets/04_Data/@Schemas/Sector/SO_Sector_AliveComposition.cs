using System.Collections.Generic;
using MapNav.Ecs;
using UnityEngine;

// 한 진영의 잡몹 로스터(무엇을, 어떤 비율로). 섹터는 "몇 마리(용량)"만 알고, 이 SO가 "무엇을"을 담당한다.
// 배경 시뮬은 숫자만 굴리므로 이 구성은 실체화(플레이어 진입) 시점에만 소비된다.
// 키 확장(등급/지역/침식단계별)은 나중 — 일단 진영당 하나.
[CreateAssetMenu(fileName = "SO_Sector_AliveComposition", menuName = "Game/Sector/AliveComposition")]
public class SO_Sector_AliveComposition : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        [SerializeField] private SO_Unit_Data unit;
        [SerializeField, Min(0f)] private float weight;
        public SO_Unit_Data Unit => unit;
        public float Weight => Mathf.Max(0f, weight);
    }

    [SerializeField] private Entry[] entries;

    // headCount를 가중치 비율대로 정수 배분(최대잉여법)해 스폰 엔트리로 변환한다.
    public NavAgentSpawnEntry[] Expand(NavFaction faction, int headCount)
    {
        if (entries == null || entries.Length == 0 || headCount <= 0)
            return null;

        float totalWeight = 0f;
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].Unit != null) totalWeight += entries[i].Weight;
        if (totalWeight <= 0f)
            return null;

        // 1차: 내림 배분 + 잔여 기록.
        var counts = new int[entries.Length];
        var remainders = new float[entries.Length];
        int assigned = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].Unit == null) continue;
            float exact = headCount * (entries[i].Weight / totalWeight);
            int floor = Mathf.FloorToInt(exact);
            counts[i] = floor;
            remainders[i] = exact - floor;
            assigned += floor;
        }

        // 2차: 남은 자리를 잔여 큰 순으로 +1 (최대잉여법, 각 항목 최대 한 번).
        int leftover = headCount - assigned;
        while (leftover > 0)
        {
            int best = -1;
            float bestRem = -1f;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Unit == null) continue;
                if (remainders[i] > bestRem)
                {
                    bestRem = remainders[i];
                    best = i;
                }
            }
            if (best < 0) break;
            counts[best]++;
            remainders[best] = -1f;
            leftover--;
        }

        var result = new List<NavAgentSpawnEntry>();
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].Unit == null || counts[i] <= 0) continue;
            result.Add(new NavAgentSpawnEntry(entries[i].Unit, counts[i], faction));
        }
        return result.Count > 0 ? result.ToArray() : null;
    }
}
