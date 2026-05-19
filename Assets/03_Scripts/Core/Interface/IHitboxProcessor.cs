using UnityEngine;

public interface IHitboxProcessor
{
    bool Process(SO_AttackData data, Transform attacker);
}
