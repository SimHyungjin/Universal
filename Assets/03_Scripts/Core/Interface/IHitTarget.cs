using UnityEngine;

public interface IHitTarget
{
    void ReceiveHit(Vector3 attackerPos, Vector3 attackerForward, SO_AttackData data);
}
