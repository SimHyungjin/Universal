using System;
using System.Collections.Generic;
using MapNav.Ecs;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public struct NavAgentSpawnEntry
{
    [FormerlySerializedAs("stats")]
    [SerializeField] private SO_Unit_Data data;
    [SerializeField] private int count;
    [Tooltip("체크 시 아군(Ally)으로 소환. 기본은 적(Enemy). " +
             "NavFaction.Ally=0이라 enum 필드를 쓰면 기존 에셋이 전부 Ally로 읽히므로 bool로 저장한다.")]
    [SerializeField] private bool ally;

    public NavAgentSpawnEntry(SO_Unit_Data data, int count, NavFaction faction)
    {
        this.data = data;
        this.count = Mathf.Max(0, count);
        ally = faction == NavFaction.Ally;
    }

    public SO_Unit_Data Data => data;
    public SO_Unit_Stats Stats => data != null ? data.StatsData : null;
    public int Count => Mathf.Max(0, count);
    public NavFaction Faction => ally ? NavFaction.Ally : NavFaction.Enemy;
}

[Serializable]
public struct EliteSpawnEntry
{
    [SerializeField] private SO_Character_Data character;
    [SerializeField] private NavFaction faction;
    [SerializeField] private int count;
    [Tooltip("이 배치 슬롯을 보스로 시작(본진 상주·소집 제외). 보스는 캐릭터 본질이 아니라 배치/시나리오 역할이라 여기서 정한다.")]
    [SerializeField] private bool isBoss;

    public SO_Character_Data Character => character;
    public NavFaction Faction => faction;
    public int Count => Mathf.Max(0, count);
    public bool IsBoss => isBoss;
}

public class Sector : MonoBehaviour
{
    [SerializeField] private string displayName;

    [Header("Erosion")]
    [Tooltip("이 섹터의 배경 병력 상한(= 최대 몇 마리). 침식 시작 시 점령 진영의 Total로 채워진다. 0이면 SO_GameStart_Settings의 기본값 사용.")]
    [SerializeField] private int capacity;

    [Header("Minimap")]
    [Tooltip("미니맵에 그릴 이 섹터의 방 이미지(회전 전 기준). 비우면 사각형 폴백으로 그림.")]
    [SerializeField] private Sprite minimapSprite;
    [Tooltip("미니맵 방 위에 얹을 프레임(테두리) 이미지. 점령 게이지/상태 색은 이 프레임에만 칠해지고 " +
             "내부(minimapSprite)는 원색을 유지한다. 비우면 기존처럼 방 이미지 전체에 색을 입힌다.")]
    [SerializeField] private Sprite minimapFrameSprite;
    [Tooltip("미니맵에서 이 섹터가 차지하는 로컬 셀 목록(회전 전 기준). 비우면 (0,0) 1칸. " +
             "L자 등은 앵커 셀을 (0,0)으로 두고 나머지를 오프셋으로 추가.")]
    [SerializeField] private Vector2Int[] minimapFootprint;

    private static readonly Vector2Int[] SingleCell = { Vector2Int.zero };

    private SectorGate[] _gates;
    private MapNavigationAuthoring _navAuthoring;

    public string DisplayName       => displayName;
    public int Capacity             => capacity;
    public SectorGate[] Gates
    {
        get
        {
            EnsureRuntimeReferences();
            return _gates;
        }
    }

    /// <summary>이 섹터 자신의 nav 그래프. 진입 시 ECS 싱글톤이 이 블롭으로 교체된다.</summary>
    public MapNavigationAuthoring NavAuthoring
    {
        get
        {
            EnsureRuntimeReferences();
            return _navAuthoring;
        }
    }

    /// <summary>미니맵에 그릴 방 이미지(회전 전 기준). 없으면 렌더러가 사각형으로 폴백.</summary>
    public Sprite MinimapSprite => minimapSprite;

    /// <summary>미니맵 방 위에 얹을 프레임(테두리) 이미지. 상태 색은 이 프레임에만 칠해진다. 없으면 방 이미지 전체를 칠한다.</summary>
    public Sprite MinimapFrameSprite => minimapFrameSprite;

    /// <summary>회전 전 기준의 로컬 footprint 셀. 미설정이면 1칸 정사각형.</summary>
    public IReadOnlyList<Vector2Int> MinimapFootprint
        => (minimapFootprint != null && minimapFootprint.Length > 0) ? minimapFootprint : SingleCell;

    private void Awake()
    {
        EnsureRuntimeReferences();
    }

    private void EnsureRuntimeReferences()
    {
        _gates ??= GetComponentsInChildren<SectorGate>(true);
        _navAuthoring ??= GetComponentInChildren<MapNavigationAuthoring>(true);
    }

    public SectorGate GetGate(GateDirection dir)
    {
        foreach (SectorGate gate in _gates)
            if (gate.Direction == dir) return gate;
        return null;
    }

    public void FinalizeGates()
    {
        foreach (SectorGate gate in _gates)
            gate.DeactivateIfUnconnected();
    }
}
