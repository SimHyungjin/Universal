using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Popup_Tutorial : UI_Popup
{
    [Header("슬라이드 데이터")]
    [SerializeField] private SO_Tutorial[] _slides;

    [Header("UI 참조")]
    [SerializeField] private TextMeshProUGUI _titleText;
    [SerializeField] private Image _slideImage;
    [SerializeField] private TextMeshProUGUI _descriptionText;

    [Header("네비게이션")]
    [SerializeField] private Button _prevButton;
    [SerializeField] private Button _nextButton;
    [SerializeField] private Button _confirmButton;
    [SerializeField] private GameObject _dotContainer;
    [SerializeField] private GameObject _dotPrefab;

    [Header("닷 색상")]
    [SerializeField] private Color _dotActiveColor = Color.white;
    [SerializeField] private Color _dotInactiveColor = new Color(1f, 1f, 1f, 0.3f);

    private int _currentIndex = 0;
    private bool _isReachedLast = false;
    private Image[] _dots;

    public override void Open()
    {
        base.Open();
        _isReachedLast = false;
        BuildDots();
        ShowSlide(0);

        _prevButton.onClick.AddListener(OnPrev);
        _nextButton.onClick.AddListener(OnNext);
        _confirmButton.onClick.AddListener(OnConfirm);
    }

    private void BuildDots()
    {
        foreach (Transform child in _dotContainer.transform)
            Destroy(child.gameObject);

        _dots = new Image[_slides.Length];
        for (int i = 0; i < _slides.Length; i++)
        {
            GameObject dot = Instantiate(_dotPrefab, _dotContainer.transform);
            dot.SetActive(true);
            _dots[i] = dot.GetComponent<Image>();

            int index = i;
            dot.GetComponent<Button>().onClick.AddListener(() => ShowSlide(index));
        }
    }

    private void ShowSlide(int index)
    {
        if (_slides == null || _slides.Length == 0) return;

        _currentIndex = Mathf.Clamp(index, 0, _slides.Length - 1);
        SO_Tutorial data = _slides[_currentIndex];

        _titleText.text = data.Title;
        _slideImage.sprite = data.Image;
        _slideImage.gameObject.SetActive(data.Image != null);
        _descriptionText.text = data.Description;

        _prevButton.gameObject.SetActive(_currentIndex > 0);

        bool isLast = _currentIndex == _slides.Length - 1;
        _nextButton.gameObject.SetActive(!isLast);

        if (isLast) _isReachedLast = true;
        _confirmButton.gameObject.SetActive(_isReachedLast);

        for (int i = 0; i < _dots.Length; i++)
            _dots[i].color = (i == _currentIndex) ? _dotActiveColor : _dotInactiveColor;
    }

    private void OnPrev() => ShowSlide(_currentIndex - 1);
    private void OnNext() => ShowSlide(_currentIndex + 1);
    private void OnConfirm() => Close();
}