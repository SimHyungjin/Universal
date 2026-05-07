using UnityEngine;

public class UI_Utility_CompositeLayoutElement : MonoBehaviour
{
    public enum LayoutType { AnchorFixed, AspectFixed, Stretch }
    
    [Header("Layout Setting")]
    public LayoutType type = LayoutType.AspectFixed;

    [Tooltip("AnchorFixed일 때 차지할 너비 비율 (0.3이면 가용 너비의 30%)")]
    public float anchorRatio = 0.3f;

    [Tooltip("AspectFixed일 때 가로/세로 비율 (1이면 정사각형)")]
    public float aspect = 1f;

    private void OnValidate()
    {
        // 인스펙터 수정 시 즉시 반영을 위해 부모 레이아웃 리빌드 요청
        var layout = GetComponentInParent<UI_Utility_CompositeLayout>();
        if (layout != null) layout.RequestRebuild();
    }
}