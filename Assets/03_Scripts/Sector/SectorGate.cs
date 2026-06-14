using UnityEngine;

public enum GateDirection { North = 0, South = 1, East = 2, West = 3 }

[RequireComponent(typeof(Collider))]
public class SectorGate : MonoBehaviour
{
    private const float PairCooldownDuration = 2f;

    [SerializeField] private GateDirection direction;
    [Tooltip("이 게이트로 도착했을 때 플레이어가 착지할 위치. 비우면 게이트 전방 2m.")]
    [SerializeField] private Transform spawnPoint;

    private SectorGate _targetGate;
    private float _pairCooldownUntil;

    public GateDirection Direction => direction;
    public bool IsConnected => _targetGate != null;
    public SectorGate ConnectedGate => _targetGate;

    public Sector Sector        => GetComponentInParent<Sector>();
    public Vector3 SpawnPosition => spawnPoint != null
        ? spawnPoint.position
        : transform.position + transform.forward * 2f;

    public void Connect(SectorGate targetGate)
    {
        _targetGate = targetGate;
        gameObject.SetActive(targetGate != null);
    }

    public void DeactivateIfUnconnected()
    {
        if (_targetGate == null) gameObject.SetActive(false);
    }

    public void StartPairCooldown()
    {
        float cooldownUntil = Time.time + PairCooldownDuration;
        _pairCooldownUntil = cooldownUntil;

        if (_targetGate != null)
            _targetGate._pairCooldownUntil = cooldownUntil;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryEnter(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryEnter(other);
    }

    private void TryEnter(Collider other)
    {
        if (_targetGate == null) return;
        SectorManager manager = SectorManager.Instance;
        if (manager == null) return;
        if (manager.IsTransitioning) return;
        if (Time.time < _pairCooldownUntil || Time.time < _targetGate._pairCooldownUntil) return;
        Character_PlayerControl player = ResolvePlayer(other);
        if (player == null) return;
        if (!player.TryGetComponent(out Character_ActionHandler actionHandler) || !actionHandler.CanEnterSectorGate) return;

        StartPairCooldown();
        manager.Enter(_targetGate);
    }

    private static Character_PlayerControl ResolvePlayer(Collider other)
    {
        if (other == null)
            return null;

        if (other.attachedRigidbody != null
            && other.attachedRigidbody.TryGetComponent(out Character_PlayerControl rigidbodyPlayer))
        {
            return rigidbodyPlayer;
        }

        return other.GetComponentInParent<Character_PlayerControl>();
    }
}
