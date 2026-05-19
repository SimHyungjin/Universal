using UnityEngine;

public interface IDamageable
{
    void TakeDamage(float amount, Vector3 hitSource, float knockbackForce = 0f);
}
