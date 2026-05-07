using UnityEngine;

public class UI : MonoBehaviour
{
    public RectTransform Rect        { get; private set; }
    public bool          IsInitialized { get; private set; }

    protected virtual void Awake() { Initialize(); }

    public virtual bool Initialize()
    {
        if (IsInitialized) return false;
        Rect = GetComponent<RectTransform>();
        IsInitialized = true;
        return true;
    }

    #region Rect Helpers

    public UI SetRectAnchor(Vector2 anchorMin, Vector2 anchorMax)
    {
        Initialize();
        Rect.anchorMin = anchorMin;
        Rect.anchorMax = anchorMax;
        return this;
    }

    public UI SetRectPivot(Vector2 pivot)
    {
        Initialize();
        Rect.pivot = pivot;
        return this;
    }

    public UI SetRectAnchoredPosition(Vector2 position)
    {
        Initialize();
        Rect.anchoredPosition = position;
        return this;
    }

    public UI SetSize(Vector2 size)
    {
        Initialize();
        Rect.sizeDelta = size;
        return this;
    }

    public UI SetOffset(Vector2 offsetMin, Vector2 offsetMax)
    {
        Initialize();
        Rect.offsetMin = offsetMin;
        Rect.offsetMax = offsetMax;
        return this;
    }

    #endregion
}
