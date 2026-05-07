using UnityEngine;

[ExecuteAlways]
public class UI_Utility_AspectFlexLayoutGroupElement : MonoBehaviour
{
    [Min(0f)]
    public float ratio = 1f;

    private UI_Utility_AspectFlexLayoutGroup _parent;

    private void OnEnable()
    {
        CacheParent();
        SetDirty();
    }

    private void OnDisable()
    {
        SetDirty();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        CacheParent();
        SetDirty();
    }
#endif

    private void CacheParent()
    {
        if (_parent == null)
            _parent = GetComponentInParent<UI_Utility_AspectFlexLayoutGroup>();
    }

    public float GetRatio()
    {
        return ratio <= 0f ? 0f : ratio;
    }

    private void SetDirty()
    {
        if (_parent != null)
            _parent.SetDirtyExternal();
    }
}