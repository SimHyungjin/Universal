using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 다이나믹 가상 조이스틱.
/// 이 컴포넌트는 터치 영역(큰 패널)에 부착한다.
/// 손가락이 닿은 위치에 joystickRoot가 이동·표시되고, 손가락을 떼면 숨긴다.
/// radius = min(joystickRoot 가로·세로) / 2 - min(knob 가로·세로) / 2 로 자동 계산.
/// </summary>
public sealed class UI_Joystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private RectTransform joystickRoot;
    [SerializeField] private RectTransform knob;

    /// <summary>중심으로부터 입력을 무시할 비율 (0 = 없음, 0.1 = 반지름의 10%).</summary>
    [SerializeField, Range(0f, 0.2f)] private float deadZoneRatio = 0.05f;

    /// <summary>정규화된 방향 벡터 (-1 ~ 1). 입력 없을 때 Vector2.zero.</summary>
    public event Action<Vector2> OnDirectionChanged;

    private RectTransform _touchArea;
    private Canvas        _canvas;
    private int           _pointerId = int.MinValue;
    private float         _radius;

    // ───────────────────────────────────────────────
    #region Lifecycle

    private void Awake()
    {
        _touchArea = GetComponent<RectTransform>();
        _canvas    = GetComponentInParent<Canvas>(includeInactive: true);

        if (joystickRoot != null)
            joystickRoot.gameObject.SetActive(false);
    }

    private void Start()
    {
        CalculateRadius();
    }

    private void OnRectTransformDimensionsChange()
    {
        CalculateRadius();
    }

    #endregion

    // ───────────────────────────────────────────────
    #region Radius

    private void CalculateRadius()
    {
        _touchArea ??= GetComponent<RectTransform>();
        if (joystickRoot == null) return;

        float bgRadius   = Mathf.Min(joystickRoot.rect.width, joystickRoot.rect.height) * 0.5f;
        float knobRadius = knob != null
            ? Mathf.Min(knob.rect.width, knob.rect.height) * 0.5f
            : 0f;
        _radius = bgRadius - knobRadius;
    }

    #endregion

    // ───────────────────────────────────────────────
    #region Pointer Events

    public void OnPointerDown(PointerEventData e)
    {
        if (_pointerId != int.MinValue) return;
        _pointerId = e.pointerId;

        // 터치 위치로 joystickRoot 이동 후 표시
        var cam = GetCam();
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_touchArea, e.position, cam, out var local);
        if (joystickRoot != null)
        {
            joystickRoot.anchoredPosition = local;
            joystickRoot.gameObject.SetActive(true);
        }

        if (knob != null) knob.anchoredPosition = Vector2.zero;
        OnDirectionChanged?.Invoke(Vector2.zero);
    }

    public void OnDrag(PointerEventData e)
    {
        if (e.pointerId != _pointerId) return;
        UpdateKnob(e.position);
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (e.pointerId != _pointerId) return;
        _pointerId = int.MinValue;

        if (joystickRoot != null)
            joystickRoot.gameObject.SetActive(false);

        ResetKnob();
    }

    #endregion

    // ───────────────────────────────────────────────
    #region Internal

    private void UpdateKnob(Vector2 screenPos)
    {
        if (joystickRoot == null) return;

        // joystickRoot 기준 로컬 좌표로 변환
        var cam = GetCam();
        RectTransformUtility.ScreenPointToLocalPointInRectangle(joystickRoot, screenPos, cam, out var local);

        float   deadZone  = _radius * deadZoneRatio;
        Vector2 direction = local.magnitude < deadZone
            ? Vector2.zero
            : Vector2.ClampMagnitude(local / _radius, 1f);

        if (knob != null)
            knob.anchoredPosition = Vector2.ClampMagnitude(local, _radius);

        OnDirectionChanged?.Invoke(direction);
    }

    private void ResetKnob()
    {
        if (knob != null) knob.anchoredPosition = Vector2.zero;
        OnDirectionChanged?.Invoke(Vector2.zero);
    }

    private Camera GetCam()
        => _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

    #endregion
}
