using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 섹터 생성 시 뽑아 쓸 섹터 프리팹 풀.
/// 디자이너가 변종 방을 추가하고 가중치를 조절하는 단일 진입점.
/// </summary>
[CreateAssetMenu(fileName = "SO_Sector_Catalog", menuName = "Game/Sector/Sector Catalog")]
public sealed class SO_Sector_Catalog : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        public Sector prefab;
        [Tooltip("가중 랜덤 비중. 0 이하면 1로 취급.")]
        public float  weight;
    }

    [SerializeField] private Entry[] entries;

    public IReadOnlyList<Entry> Entries => entries;
    public bool HasEntries => entries != null && entries.Length > 0;

    /// <summary>가중 랜덤으로 프리팹 하나 선택. 유효 항목이 없으면 null.</summary>
    public Sector PickWeighted()
    {
        if (!HasEntries) return null;

        float total = 0f;
        foreach (Entry e in entries)
            if (e.prefab != null) total += Mathf.Max(e.weight, 1f);

        if (total <= 0f) return null;

        float roll = Random.value * total;
        foreach (Entry e in entries)
        {
            if (e.prefab == null) continue;
            roll -= Mathf.Max(e.weight, 1f);
            if (roll <= 0f) return e.prefab;
        }

        // 부동소수점 잔차 보호: 마지막 유효 항목 반환
        for (int i = entries.Length - 1; i >= 0; i--)
            if (entries[i].prefab != null) return entries[i].prefab;

        return null;
    }
}
