using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 생성 시점에 한 번 구워지는 미니맵 토폴로지 스냅샷.
/// 3D 씬과 분리된 순수 데이터라 UI는 이 모델만 보고 그린다.
/// 좌표는 전부 정수 그리드 셀 → 3D 배치와 동기화가 자동 보장된다.
/// </summary>
public sealed class MinimapModel
{
    public sealed class Node
    {
        public int          Index;        // 섹터 인덱스(엣지가 참조)
        public Sector       Sector;       // 현재/방문 상태 매칭용
        public Vector2Int   AnchorCell;   // 배치 셀(=positions[i]). 통로 끝점 기준
        public Vector2Int[] Cells;        // 회전 적용된 절대 그리드 셀들(footprint)
        public bool         IsStart;      // 시작 섹터 여부
        public Sprite       Sprite;       // 방 이미지(없으면 사각형 폴백)
        public Sprite       FrameSprite;  // 방 위에 얹을 프레임(테두리). 상태 색은 이 프레임에만 칠해진다. 없으면 방 전체를 칠함

        public int          RotationSteps; // 90° 회전 스텝(0~3). 스프라이트 회전용
        public Vector2      WorldSize;    // nav 바닥 월드 크기(회전 전 x,z). 0이면 셀 기본 박스 폴백
        public Vector2      LocalCenter;  // nav 바닥 중심의 섹터 원점 대비 오프셋(회전 전 x,z)
    }

    public struct Edge
    {
        public int A; // 노드 인덱스
        public int B;
    }

    public Vector2Int GridSize;
    public float      CellSize;  // 월드↔셀 변환용(마커 좌표 동기화). GridToWorld와 동일 스케일
    public List<Node> Nodes = new();
    public List<Edge> Edges = new();
}
