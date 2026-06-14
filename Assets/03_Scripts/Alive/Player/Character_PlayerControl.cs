using UnityEngine;

// 현재 플레이어가 빙의(possession)한 캐릭터를 표시하는 마커. PlayerController가 빙의/해제 시 붙였다 뗀다.
// "플레이어 아바타가 누구인가"를 콜라이더/Find 조회로 식별하던 곳(SectorGate, SectorManager,
// NavRuntimeBootstrap, Elite_Brain)이 Player_Actor 대신 이 태그를 본다. 로직은 없다 — 정체성 표식만.
[DisallowMultipleComponent]
public sealed class Character_PlayerControl : MonoBehaviour
{
}
