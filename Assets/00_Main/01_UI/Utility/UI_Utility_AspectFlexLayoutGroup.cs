using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UI_Utility_AspectFlexLayoutGroup : LayoutGroup
{
    public enum Direction
    {
        Horizontal,
        Vertical
    }

    [SerializeField] private Direction direction = Direction.Horizontal;

    [Range(0f, 1f)]
    public float spacingRatio = 0f;

    private readonly List<UI_Utility_AspectFlexLayoutGroupElement> _ratioCache = new List<UI_Utility_AspectFlexLayoutGroupElement>();
    private bool _isDirty = true;

    public override void CalculateLayoutInputHorizontal()
    {
        base.CalculateLayoutInputHorizontal();
        RefreshCacheIfNeeded();
    }

    public override void CalculateLayoutInputVertical()
    {
        RefreshCacheIfNeeded();
    }

    public override void SetLayoutHorizontal()
    {
        if (direction == Direction.Horizontal)
            ApplyLayout();
    }

    public override void SetLayoutVertical()
    {
        if (direction == Direction.Vertical)
            ApplyLayout();
    }

    private void ApplyLayout()
    {
        int count = rectChildren.Count;
        if (count == 0) return;

        float parentWidth = rectTransform.rect.width;
        float parentHeight = rectTransform.rect.height;

        float parentMain = direction == Direction.Horizontal ? parentWidth : parentHeight;
        float parentCross = direction == Direction.Horizontal ? parentHeight : parentWidth;

        float spacing = parentMain * spacingRatio;
        float totalSpacing = spacing * (count - 1);

        float baseCross = parentCross;

        float totalMain = totalSpacing;

        float[] sizes = new float[count];

        for (int i = 0; i < count; i++)
        {
            float ratio = _ratioCache[i] != null ? _ratioCache[i].GetRatio() : 1f;

            float size = baseCross * ratio;
            sizes[i] = size;

            totalMain += size;
        }

        float scale = 1f;
        if (totalMain > parentMain)
        {
            scale = parentMain / totalMain;
        }

        float scaledCross = baseCross * scale;
        float scaledMainTotal = totalMain * scale;

        float startMain = GetStartOffset(direction == Direction.Horizontal ? 0 : 1, scaledMainTotal);
        float startCross = GetStartOffset(direction == Direction.Horizontal ? 1 : 0, scaledCross);

        float pos = startMain;

        for (int i = 0; i < count; i++)
        {
            RectTransform child = rectChildren[i];

            float size = sizes[i] * scale;

            if (direction == Direction.Horizontal)
            {
                SetChildAlongAxis(child, 0, pos, size);
                SetChildAlongAxis(child, 1, startCross, scaledCross);
            }
            else
            {
                SetChildAlongAxis(child, 1, pos, size);
                SetChildAlongAxis(child, 0, startCross, scaledCross);
            }

            pos += size + spacing * scale;
        }
    }

    private void RefreshCacheIfNeeded()
    {
        if (!_isDirty) return;

        _ratioCache.Clear();

        for (int i = 0; i < rectChildren.Count; i++)
        {
            _ratioCache.Add(rectChildren[i].GetComponent<UI_Utility_AspectFlexLayoutGroupElement>());
        }

        _isDirty = false;
    }

    public void SetDirtyExternal()
    {
        _isDirty = true;
        SetDirty();
    }

    protected override void OnTransformChildrenChanged()
    {
        base.OnTransformChildrenChanged();
        _isDirty = true;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        _isDirty = true;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        _isDirty = true;
    }
#endif
}