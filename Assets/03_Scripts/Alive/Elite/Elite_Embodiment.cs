using MapNav.Ecs;
using UnityEngine;

// 장수 실체(현재 섹터에서만 인스턴스화)의 ECS 무관 다리.
// 피격/사망 권위는 Character_Vitals + Character_ActionHandler가 갖고, 여기서는
// 그 결과(체력)를 Elite_State(백그라운드 진실)에 미러만 한다.
// 사망 후 디스폰/수거는 Elite_Manager.ReapDeadElites가 담당한다(몸체 코루틴에 묶지 않아 누수 방지).
[DisallowMultipleComponent]
public sealed class Elite_Embodiment : MonoBehaviour
{
    public Elite_State State { get; private set; }

    private Character_Vitals _vitals;

    public void Bind(Elite_State state)
    {
        State = state;
        if (State == null)
            return;

        SyncState();

        _vitals = GetComponent<Character_Vitals>();
        if (_vitals == null)
            return;

        // 진영/시작체력(Elite_State 기준)과 스탯을 ActionHandler 경유로 주입한다(플레이어 빙의와 동일 통로).
        // 스탯/브레이크 값은 EmbodyAsync가 먼저 호출한 SetCharacterData(State.Character)에서 온다.
        Character_ActionHandler actionHandler = GetComponent<Character_ActionHandler>();
        actionHandler?.ConfigureVitals(State.Faction, State.Health);

        _vitals.OnHealthChanged += HandleHealthChanged;

        Elite_HealthBar healthBar = GetComponent<Elite_HealthBar>();
        if (healthBar == null)
            healthBar = gameObject.AddComponent<Elite_HealthBar>();
        healthBar.Bind(_vitals, State.Faction);
    }

    private void Update()
    {
        SyncState();
    }

    private void OnDestroy()
    {
        if (_vitals != null)
            _vitals.OnHealthChanged -= HandleHealthChanged;

        if (State != null && State.Embodiment == gameObject)
            State.DetachEmbodiment();
    }

    private void SyncState()
    {
        if (State == null) return;

        State.WorldPosition = transform.position;
        State.Forward = transform.forward.sqrMagnitude > 0.0001f
            ? transform.forward.normalized
            : Vector3.forward;
    }

    private void HandleHealthChanged(float current, float max)
    {
        if (State != null)
            State.Health = current; // Health<=0이면 Elite_Manager.ReapDeadElites가 연출 후 수거.
    }
}

[DisallowMultipleComponent]
public sealed class Elite_HealthBar : MonoBehaviour
{
    private const float MinWidth = 1.8f;
    private const float MaxWidth = 4f;
    private const float BackgroundThickness = 0.22f;
    private const float FillThickness = 0.14f;
    private const float BreakBarYOffset = -0.28f;
    private const float BreakBackgroundThickness = 0.12f;
    private const float BreakFillThickness = 0.08f;

    private static Material _lineMaterial;

    private Character_Vitals _vitals;
    private Transform _root;
    private LineRenderer _background;
    private LineRenderer _fill;
    private LineRenderer _breakBackground;
    private LineRenderer _breakFill;
    private float _width;
    private float _breakFlashTimer;

    public void Bind(Character_Vitals vitals, NavFaction faction)
    {
        if (_vitals != null)
        {
            _vitals.OnHealthChanged -= HandleHealthChanged;
            _vitals.OnBreakChanged -= HandleBreakChanged;
            _vitals.OnBroken -= HandleBroken;
        }

        _vitals = vitals;
        EnsureVisuals(faction);

        if (_vitals == null)
        {
            _root.gameObject.SetActive(false);
            return;
        }

        _vitals.OnHealthChanged += HandleHealthChanged;
        _vitals.OnBreakChanged += HandleBreakChanged;
        _vitals.OnBroken += HandleBroken;
        HandleHealthChanged(_vitals.Health, _vitals.MaxHealth);
        HandleBreakChanged(_vitals.Break, _vitals.BreakMax);
    }

    private void OnDestroy()
    {
        if (_vitals != null)
        {
            _vitals.OnHealthChanged -= HandleHealthChanged;
            _vitals.OnBreakChanged -= HandleBreakChanged;
            _vitals.OnBroken -= HandleBroken;
        }
    }

    private void LateUpdate()
    {
        if (_root == null || Camera.main == null)
            return;

        _root.rotation = Camera.main.transform.rotation;
        TickBreakFlash();
    }

    private void EnsureVisuals(NavFaction faction)
    {
        if (_root == null)
        {
            CharacterController controller = GetComponent<CharacterController>();
            float bodyRadius = controller != null
                ? controller.radius
                : (_vitals != null ? _vitals.BodyRadius : 0.5f);
            _width = Mathf.Clamp(bodyRadius * 3f, MinWidth, MaxWidth);

            Vector3 localPosition = controller != null
                ? controller.center + Vector3.up * (controller.height * 0.5f + 0.35f)
                : Vector3.up * (bodyRadius * 2f + 0.35f);

            var rootObject = new GameObject("Elite Health Bar");
            _root = rootObject.transform;
            _root.SetParent(transform, false);
            _root.localPosition = localPosition;

            _background = CreateLine("Background", BackgroundThickness, new Color(0.03f, 0.04f, 0.05f, 0.92f), 50);
            Color fillColor = faction == NavFaction.Ally
                ? new Color(0.1f, 0.72f, 1f, 1f)
                : new Color(1f, 0.18f, 0.18f, 1f);
            _fill = CreateLine("Fill", FillThickness, fillColor, 51);
            _breakBackground = CreateLine(
                "Break Background",
                BreakBackgroundThickness,
                new Color(0.04f, 0.035f, 0.02f, 0.86f),
                52,
                new Vector3(0f, BreakBarYOffset, 0f));
            _breakFill = CreateLine(
                "Break Fill",
                BreakFillThickness,
                new Color(1f, 0.78f, 0.18f, 1f),
                53,
                new Vector3(0f, BreakBarYOffset, 0f));
        }

        _root.gameObject.SetActive(true);
        SetLine(_background, 0f, 1f);
        SetLine(_breakBackground, 0f, 1f);
    }

    private LineRenderer CreateLine(string objectName, float width, Color color, int sortingOrder)
        => CreateLine(objectName, width, color, sortingOrder, Vector3.zero);

    private LineRenderer CreateLine(string objectName, float width, Color color, int sortingOrder, Vector3 localPosition)
    {
        var lineObject = new GameObject(objectName);
        lineObject.transform.SetParent(_root, false);
        lineObject.transform.localPosition = localPosition;

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.positionCount = 2;
        line.widthMultiplier = width;
        line.numCapVertices = 2;
        line.startColor = color;
        line.endColor = color;
        line.sharedMaterial = ResolveLineMaterial();
        line.sortingOrder = sortingOrder;
        return line;
    }

    private void HandleHealthChanged(float current, float max)
    {
        if (_root == null)
            return;

        float ratio = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        _root.gameObject.SetActive(ratio > 0f);
        SetLine(_fill, 0f, ratio);
    }

    private void HandleBreakChanged(float current, float max)
    {
        bool hasBreak = max > 0f;
        if (_breakBackground != null)
            _breakBackground.gameObject.SetActive(hasBreak);
        if (_breakFill != null)
            _breakFill.gameObject.SetActive(hasBreak);
        if (!hasBreak)
            return;

        float ratio = Mathf.Clamp01(current / max);
        SetLine(_breakFill, 0f, ratio);
        if (_breakFlashTimer <= 0f && _breakFill != null)
            SetLineColor(_breakFill, _vitals != null && _vitals.IsBroken ? BreakVulnerableColor : BreakReadyColor);
    }

    private void HandleBroken()
    {
        _breakFlashTimer = BreakFlashDuration;
        if (_breakFill != null)
        {
            _breakFill.gameObject.SetActive(true);
            SetLine(_breakFill, 0f, 1f);
            SetLineColor(_breakFill, BreakFlashColor);
        }
    }

    private void TickBreakFlash()
    {
        if (_breakFlashTimer <= 0f || _breakFill == null)
            return;

        _breakFlashTimer -= Time.deltaTime;
        float t = 1f - Mathf.Clamp01(_breakFlashTimer / BreakFlashDuration);
        SetLineColor(_breakFill, Color.Lerp(BreakFlashColor, BreakVulnerableColor, t));

        if (_breakFlashTimer <= 0f)
            SetLineColor(_breakFill, BreakVulnerableColor);
    }

    private void SetLine(LineRenderer line, float startRatio, float endRatio)
    {
        if (line == null)
            return;

        float left = -_width * 0.5f;
        line.SetPosition(0, new Vector3(left + _width * startRatio, 0f, 0f));
        line.SetPosition(1, new Vector3(left + _width * endRatio, 0f, 0f));
    }

    private static void SetLineColor(LineRenderer line, Color color)
    {
        if (line == null)
            return;

        line.startColor = color;
        line.endColor = color;
    }

    private static readonly Color BreakReadyColor = new(1f, 0.78f, 0.18f, 1f);
    private static readonly Color BreakFlashColor = Color.white;
    private static readonly Color BreakVulnerableColor = new(1f, 0.08f, 0.04f, 1f);
    private const float BreakFlashDuration = 0.28f;

    private static Material ResolveLineMaterial()
    {
        if (_lineMaterial != null)
            return _lineMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");

        _lineMaterial = new Material(shader) { name = "Elite Health Bar Material" };
        return _lineMaterial;
    }
}
