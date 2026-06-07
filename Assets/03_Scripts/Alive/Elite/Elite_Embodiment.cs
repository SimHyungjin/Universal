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

        // 전투 스탯은 캐릭터(SO_Character_Data)에서 온다. 진영/시작체력은 Elite_State 기준.
        SO_Character_Stats stats = State.Data != null && State.Data.Character != null
            ? State.Data.Character.StatsData
            : null;
        _vitals.Configure(
            stats != null ? stats.MaxHealth : _vitals.MaxHealth,
            stats != null ? stats.Defense : 0f,
            stats != null ? stats.GaugeMax : _vitals.GaugeMax,
            State.Faction,
            State.Health,
            stats != null ? stats.BodyRadius : 0.5f);

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

    private static Material _lineMaterial;

    private Character_Vitals _vitals;
    private Transform _root;
    private LineRenderer _background;
    private LineRenderer _fill;
    private float _width;

    public void Bind(Character_Vitals vitals, NavFaction faction)
    {
        if (_vitals != null)
            _vitals.OnHealthChanged -= HandleHealthChanged;

        _vitals = vitals;
        EnsureVisuals(faction);

        if (_vitals == null)
        {
            _root.gameObject.SetActive(false);
            return;
        }

        _vitals.OnHealthChanged += HandleHealthChanged;
        HandleHealthChanged(_vitals.Health, _vitals.MaxHealth);
    }

    private void OnDestroy()
    {
        if (_vitals != null)
            _vitals.OnHealthChanged -= HandleHealthChanged;
    }

    private void LateUpdate()
    {
        if (_root == null || Camera.main == null)
            return;

        _root.rotation = Camera.main.transform.rotation;
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
        }

        _root.gameObject.SetActive(true);
        SetLine(_background, 0f, 1f);
    }

    private LineRenderer CreateLine(string objectName, float width, Color color, int sortingOrder)
    {
        var lineObject = new GameObject(objectName);
        lineObject.transform.SetParent(_root, false);

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

    private void SetLine(LineRenderer line, float startRatio, float endRatio)
    {
        if (line == null)
            return;

        float left = -_width * 0.5f;
        line.SetPosition(0, new Vector3(left + _width * startRatio, 0f, 0f));
        line.SetPosition(1, new Vector3(left + _width * endRatio, 0f, 0f));
    }

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
