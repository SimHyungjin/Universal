using System.Collections.Generic;
using UnityEngine;

public interface IHitboxProcessor
{
    bool Process(SO_AttackData data, Transform attacker, AttackHitRegistry hitRegistry, float finalDamage, float targetSuspendDuration);
    bool ProcessExtra(SO_AttackData data, AttackExtraHit extra, int extraIndex, Transform attacker, AttackHitRegistry hitRegistry, float finalDamage, float targetSuspendDuration);
}

public sealed class AttackHitRegistry
{
    private readonly HashSet<int> _keys = new();

    public void Clear()
    {
        _keys.Clear();
    }

    public bool TryRegister(int key, bool hitSameTargetOnce)
        => TryRegister(key, 0, hitSameTargetOnce);

    public bool TryRegister(int key, int scope, bool hitSameTargetOnce)
    {
        unchecked
        {
            return !hitSameTargetOnce || _keys.Add((key * 397) ^ scope);
        }
    }
}
