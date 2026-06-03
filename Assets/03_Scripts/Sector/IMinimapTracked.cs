using MapNav.Ecs;
using UnityEngine;

// 미니맵 마커가 따라다닐 대상의 런타임 위치 공급자.
// Transform에 묶이지 않으므로, 현재 섹터에 실체(GameObject)가 없는 백그라운드 엔티티
// (예: 다른 섹터의 장수 Elite_State)도 마커로 그릴 수 있다.
public interface IMinimapTracked
{
    // 소속 섹터. 현재 섹터면 실제 위치로, 다른 섹터(배경)면 그 섹터 노드의 요약 배지로 집계된다.
    Sector Sector { get; }
    Vector3 WorldPosition { get; }
    Vector3 Forward { get; }

    // 진영. 배경 섹터 요약 배지에서 아군/적 개수를 나누는 데 쓴다.
    NavFaction Faction { get; }

    // 게이트 이동 중이면 출발/도착 섹터와 진행도(0~1)를 보고하고 true. 미니맵은 이때 두 섹터 노드를 잇는
    // 통로(엣지)를 따라 진행도만큼 마커를 글라이드시킨다(위상 이동). 이동 중이 아니면 false.
    bool TryGetTransition(out Sector from, out Sector to, out float t);
}
