using UnityEngine;

public class SafeArea : MonoBehaviour
{
    [Header("Safe Area Panel")]
    [SerializeField] private RectTransform _safeAreaPanel;

    [Header("Filler RectTransforms")]
    [SerializeField] private RectTransform _leftFiller;
    [SerializeField] private RectTransform _rightFiller;
    [SerializeField] private RectTransform _topFiller;
    [SerializeField] private RectTransform _bottomFiller;

    private void Awake()
    {
        if (_safeAreaPanel == null) _safeAreaPanel = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        if (Main.Safe != null)
        {
            Main.Safe.OnSafeAreaChanged += Apply;
            Apply(Main.Safe.GetCurrentData());
        }
    }

    private void OnDisable()
    {
        if (Main.Instance != null && Main.Safe != null)
            Main.Safe.OnSafeAreaChanged -= Apply;
    }

    private void Apply(SafeAreaManager.SafeAreaData data)
    {
        if (_safeAreaPanel == null) return;
        _safeAreaPanel.anchorMin = data.AnchorMin;
        _safeAreaPanel.anchorMax = data.AnchorMax;
        _safeAreaPanel.offsetMin = Vector2.zero;
        _safeAreaPanel.offsetMax = Vector2.zero;

        UpdateFiller(_leftFiller,   0f,          0f,        data.Left,  1f);
        UpdateFiller(_rightFiller,  data.Right,  0f,        1f,         1f);
        UpdateFiller(_topFiller,    0f,          data.Top,  1f,         1f);
        UpdateFiller(_bottomFiller, 0f,          0f,        1f,         data.Bottom);
    }

    private void UpdateFiller(RectTransform filler, float minX, float minY, float maxX, float maxY)
    {
        if (filler == null) return;
        filler.anchorMin = new Vector2(minX, minY);
        filler.anchorMax = new Vector2(maxX, maxY);
        filler.offsetMin = Vector2.zero;
        filler.offsetMax = Vector2.zero;
    }
}
