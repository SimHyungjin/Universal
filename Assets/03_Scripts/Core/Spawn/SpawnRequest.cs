using UnityEngine;

// 캐릭터를 월드에 실체화할 때의 배치 컨텍스트.
public readonly struct SpawnRequest
{
    public readonly Vector3 Position;
    public readonly Quaternion Rotation;
    public readonly Sector Sector; // 소속 섹터(선택). elite 등록/미니맵 매핑에 쓰인다.

    public SpawnRequest(Vector3 position, Vector3 forward, Sector sector = null)
    {
        Position = position;
        Rotation = forward.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(forward.normalized, Vector3.up)
            : Quaternion.identity;
        Sector = sector;
    }
}
