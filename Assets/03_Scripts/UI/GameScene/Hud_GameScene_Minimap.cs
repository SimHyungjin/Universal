using System.Collections.Generic;
using MapNav.Ecs;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MinimapModel을 화면 우상단에 표시하는 미니맵 뷰.
/// 배경+통로는 Texture2D에 한 번 굽고, 방은 nav 바닥 월드 크기에 비례한 오버레이 Image로 얹는다.
/// HUD에 배치된 RectTransform이 있으면 그 영역과 pivot을 그대로 따르고, 방문 경로는 통로 색으로 갱신한다.
/// </summary>
public sealed class Hud_GameScene_Minimap : MonoBehaviour
{
    private const int TexturePixelsPerCell = 28;
    private const int FallbackPanelSize = 480;
    private const float FallbackRoomScale = 0.5f;

    private static readonly Vector2 StandaloneScreenMargin = new(20f, 20f);
    private static readonly Color FallbackRoomColor = new(0.20f, 0.25f, 0.36f, 1f);
    private static readonly Color FallbackVisitedColor = new(0.42f, 0.52f, 0.72f, 1f);
    private static readonly Color FallbackCurrentColor = new(1.00f, 0.78f, 0.18f, 1f);
    private static readonly Color FallbackStartColor = new(0.30f, 0.78f, 0.42f, 1f);
    private static readonly Color TransitionColor = new(0.26f, 0.86f, 0.78f, 1f);

    [Header("Visuals")]
    [SerializeField] private RectTransform minimapRoot;
    [SerializeField] private int     corridorThickness = 2;
    [SerializeField] private float   sectorVisualScale = 2f;
    [SerializeField] private float   boundsPaddingCells = 0.25f;

    [Header("Colors")]
    [SerializeField] private Color backgroundColor = new(0.05f, 0.06f, 0.09f, 0.85f);
    [SerializeField] private Color corridorColor   = new(0.34f, 0.40f, 0.54f, 1f);
    [SerializeField] private Color unexploredTint   = new(0.34f, 0.36f, 0.45f, 1f); // 미방문 방 스프라이트 어둡게

    [Header("Markers")]
    [SerializeField] private float markerSmoothing = 12f; // 목표로 수렴하는 속도(작을수록 러프, 0이면 즉시)
    [SerializeField] private Sprite playerDirectionSprite;
    [SerializeField] private Vector2 playerDirectionSizeRatio = new(0.78f, 0.62f);
    [SerializeField] private float playerDirectionOffsetRatio = 0.42f;
    [SerializeField] private Color playerDirectionColor = Color.black;
    [SerializeField] private MinimapMarkerSettings playerMarker = MinimapMarkerSettings.DefaultPlayer;
    [SerializeField] private MinimapMarkerSettings allyEliteMarker = new(null, new Color(0f, 0.31f, 1f, 1f), 15f, 8f, 30f, false);
    [SerializeField] private MinimapMarkerSettings enemyEliteMarker = new(null, new Color(1f, 0.06f, 0f, 1f), 15f, 8f, 30f, false);

    [Header("Route movement")]
    [SerializeField] private float routeLaneOffsetPx = 16f;
    [SerializeField] private float routeBadgeOffsetPx = 16f;
    [SerializeField] private float routeBadgeSpacingPx = 18f;
    [SerializeField] private Vector2 routeArrowSize = new(18f, 30f);
    [SerializeField] private int   routeBadgeFontSize = 14;
    [SerializeField] private Color routeArrowColor = new(0.70f, 0.86f, 1f, 1f);
    [SerializeField] private Sprite routeArrowSprite;

    [Header("Strategic links")]
    [SerializeField] private Color linkAllyColor = new(0.18f, 0.55f, 0.82f, 0.52f);
    [SerializeField] private Color linkEnemyColor = new(0.72f, 0.18f, 0.16f, 0.52f);
    [SerializeField] private float linkLineWidthPx = 2f;
    [SerializeField] private Sprite hubFrameSprite;

    [Header("Background sector badge")]
    [SerializeField] private bool showSectorMobCounts = true;
    [SerializeField] private Color badgeAllyColor  = new(0.35f, 1f, 0.45f, 1f);
    [SerializeField] private Color badgeEnemyColor = new(1f, 0.4f, 0.4f, 1f);
    [SerializeField] private int   badgeFontSize   = 16;
    [SerializeField] private float badgeOffsetPx   = 9f; // 아군/적 숫자를 노드 중심 좌우로 벌리는 간격

    [Header("Sector control gauge")]
    [SerializeField] private Color gaugeAllyColor  = new(0.30f, 0.60f, 1f, 1f);   // 아군 우세 → 파랑
    [SerializeField] private Color gaugeEnemyColor = new(1f, 0.35f, 0.35f, 1f);   // 적 우세 → 빨강
    [SerializeField] private int   gaugeFontSize   = 14;
    [SerializeField] private float gaugeOffsetPx   = 11f; // 점령% 텍스트를 노드 중심 아래로 내리는 간격

    private MinimapModel _model;
    private RawImage     _image;
    private Texture2D    _texture;
    private Color32[]    _buffer;
    private RectTransform _rootRect;
    private bool          _usesHudLayout;

    private Vector2    _mapMinCell;
    private int        _texWidth;
    private int        _texHeight;
    private float      _cellSize;
    private float      _renderScale;    // 텍스처 px → 이미지 로컬 px
    private float      _worldToImagePx; // 월드 단위 → 이미지 로컬 px (마커/방 공용 스케일)

    private readonly HashSet<int> _visited = new();
    private long? _transitionEdgeKey;
    private int   _transitionFromIndex = -1; // 이동 중 방향 배지를 그릴 출발 노드
    private int   _transitionToIndex   = -1;
    private int  _currentIndex = -1;
    private bool _wasTransitioning;
    private bool _dirty;

    // 월드 좌표를 따라다니는 마커(플레이어/유닛 공용). 좌표 소스만 갈아끼우면 재사용된다.
    public sealed class Marker
    {
        public IMinimapTracked Source;
        public RectTransform   Rect;
        public Image           Image;
        public RectTransform   DirectionRect;
        public bool            RotateWithTarget;
        // 플레이어처럼 현재 섹터 게이트 전환 상태를 전역 SectorManager에서 받을지. 장수 등은 false.
        public bool            FollowsCurrentSectorTransition;
        public bool            RenderOnTop;
        public bool            Placed; // 첫 배치는 즉시(스냅), 이후엔 러프하게 수렴
    }

    // Transform을 IMinimapTracked로 감싸는 어댑터. Sector=null이라 nearest-node 폴백(레거시 마커 동작 보존).
    private sealed class TransformTracked : IMinimapTracked
    {
        private readonly Transform _t;
        public TransformTracked(Transform t) => _t = t;
        public Sector  Sector        => null;
        public Vector3 WorldPosition => _t != null ? _t.position : Vector3.zero;
        public Vector3 Forward       => _t != null ? _t.forward : Vector3.forward;
        public NavFaction Faction    => NavFaction.Ally; // 플레이어는 아군. 현재 섹터라 배지엔 안 들어감.

        // 플레이어의 게이트 전환은 FollowsCurrentSectorTransition(전역 SectorManager 상태)로 처리하므로 false.
        public bool TryGetTransition(out Sector from, out Sector to, out float t)
        {
            from = null; to = null; t = 0f;
            return false;
        }
    }

    private readonly List<Marker> _markers = new();

    private struct RouteCounts
    {
        public int Ally;
        public int Enemy;
    }

    private sealed class RouteIndicator
    {
        public RectTransform Root;
        public RectTransform ArrowRect;
        public Graphic       Arrow;
        public Text          AllyBadge;
        public Text          EnemyBadge;
    }

    private readonly Dictionary<long, RouteCounts> _routeCounts = new();
    private readonly List<RouteIndicator> _routePool = new();

    private sealed class LinkLine
    {
        public RectTransform Rect;
        public Image Image;
    }

    private readonly List<LinkLine> _linkLinePool = new();
    private readonly List<Image> _hubFramePool = new();

    // 배경 섹터 요약 배지(잡몹 병력 숫자 + 점령 게이지%). 풀에서 Text 라벨을 재사용한다.
    private readonly List<Text> _badgePool = new();
    private static Font _badgeFont;

    // 방 오버레이(스프라이트 또는 단색 박스). 상태 변화 시 색만 갱신.
    private readonly List<(int index, Image image)> _rooms = new();

    public void Init(MinimapModel model)
    {
        Clear();
        ResolveDefaultSprites();

        _model = model;
        if (_model == null || _model.Nodes.Count == 0) return;

        _cellSize = _model.CellSize;
        BuildCanvas();
        BuildTexture();
        BuildRooms();
        SyncCurrentSector();
        DrawStaticMap();
        _dirty = false;
        RefreshRoomTints();
    }

    private void ResolveDefaultSprites()
    {
#if UNITY_EDITOR
        playerDirectionSprite ??= UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/01_Assets/UI/Minimap/Arrow2.png");
        routeArrowSprite ??= UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/01_Assets/UI/Minimap/Arrow 1.png");
        hubFrameSprite ??= UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/01_Assets/UI/Frame 1.png");
#endif
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveDefaultSprites();
    }
#endif

    private void Update()
    {
        if (_model == null) return;
        SyncCurrentSector();
        SyncTransitionState();
        if (_dirty)
        {
            _dirty = false;
            DrawStaticMap();
        }
        RefreshRoomTints();  // 점령 게이지 색은 매 프레임 변하므로 항상 갱신.
        UpdateMarkers();
    }

    // ── 방 오버레이 (월드 스케일) ────────────────────────────────────────────────
    // 각 방을 nav 바닥 월드 크기에 비례해 그린다. 위치/크기 모두 월드→픽셀 단일 투영이라
    // 실제 footprint에 맞춰 그린 스프라이트가 씬과 1:1로 나오고, 플레이어 마커와도 정렬된다.

    private void BuildRooms()
    {
        foreach (MinimapModel.Node node in _model.Nodes)
        {
            var go = new GameObject("Room", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_image.rectTransform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f); // 이미지 좌하단 기준
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta        = RoomSizePx(node);            // 회전 전 크기. 회전은 아래에서 적용
            rt.anchoredPosition = RoomCenterImageLocal(node);
            // 셀 회전((x,y)->(y,-x), 시계)과 같은 방향. 비대칭 스프라이트로 부호 한 번 확인 필요.
            rt.localEulerAngles = new Vector3(0f, 0f, -90f * node.RotationSteps);

            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            if (node.Sprite != null)
            {
                img.sprite         = node.Sprite;
                img.preserveAspect = node.WorldSize.x <= 0f; // 월드 크기 알면 박스에 꽉 채움
            }
            _rooms.Add((node.Index, img));
        }
    }

    private void RefreshRoomTints()
    {
        SectorBattleManager battle = SectorBattleManager.Instance;
        for (int i = 0; i < _rooms.Count; i++)
        {
            var (index, img) = _rooms[i];
            img.color = ResolveRoomColor(_model.Nodes[index], battle);
        }
    }

    // 병력이 있는 섹터는 점령 게이지 색(적 빨강 ~ 아군 파랑)으로 칠한다. 현재 섹터는 살짝 밝게 강조.
    private Color ResolveRoomColor(MinimapModel.Node node, SectorBattleManager battle)
    {
        if (battle != null && battle.TryGetState(node.Sector, out SectorBattleState state)
            && (state.AllyTotal > 0f || state.EnemyTotal > 0f))
        {
            Color gauge = Color.Lerp(gaugeEnemyColor, gaugeAllyColor, state.GaugeNormalized);
            if (node.Index == _currentIndex)
                gauge = Color.Lerp(gauge, Color.white, 0.35f);
            return gauge;
        }

        // 병력 없는(정리된/무주공산) 섹터: 스프라이트는 원색, 아니면 기존 상태색.
        return node.Sprite != null ? Color.white : ResolveColor(node);
    }

    // nav 월드 크기 → 이미지 px. 없으면 셀 기본 박스로 폴백.
    private Vector2 RoomSizePx(MinimapModel.Node node)
    {
        if (node.Sprite != null)
        {
            float cellCanvasSize = Mathf.Max(_cellSize, 0.0001f) * sectorVisualScale;
            return Vector2.one * cellCanvasSize * _worldToImagePx;
        }

        if (node.WorldSize.x > 0f && node.WorldSize.y > 0f)
        {
            Vector2 size = node.WorldSize * sectorVisualScale;
            return size * _worldToImagePx;
        }

        float box = TexturePixelsPerCell * FallbackRoomScale * sectorVisualScale * _renderScale;
        return new Vector2(box, box);
    }

    // 방 중심 월드 좌표(섹터 원점 + 회전된 로컬 중심) → 이미지 로컬 px.
    private Vector2 RoomCenterImageLocal(MinimapModel.Node node)
    {
        if (node.Sprite != null)
            return CellToImageLocal(AnchorCenterCell(node));

        Vector2 rc = RotateLocal(node.LocalCenter, node.RotationSteps);
        Vector2 cell = AnchorCenterCell(node) + rc / Mathf.Max(_cellSize, 0.0001f);
        return CellToImageLocal(cell);
    }

    // (x,y)->(y,-x) 시계 회전. 셀/게이트 회전 컨벤션과 동일.
    private static Vector2 RotateLocal(Vector2 v, int steps)
    {
        steps &= 3;
        for (int i = 0; i < steps; i++) v = new Vector2(v.y, -v.x);
        return v;
    }

    // ── 마커 (재사용 가능한 오버레이 레이어) ─────────────────────────────────────
    // RawImage 위에 얹는 UI 점. 매 프레임 위치만 갱신한다.

    public Marker AddPlayerMarker(Transform target, SO_Character_Data data)
        => AddPlayerMarker(target, playerMarker.WithSprite(data != null ? data.MarkerSprite : null));

    public Marker AddPlayerMarker(Transform target, MinimapMarkerSettings marker)
        => target == null ? null
            : CreateMarker(new TransformTracked(target), marker, followsTransition: true, renderOnTop: true);

    public Marker AddMarker(Transform target, Color color, float worldSize = 30f, Sprite sprite = null, bool rotateWithTarget = false)
        => target == null ? null
            : CreateMarker(
                new TransformTracked(target),
                new MinimapMarkerSettings(sprite, color, worldSize, rotateWithTarget: rotateWithTarget),
                followsTransition: true,
                renderOnTop: false);

    // 장수 등 Transform 없는 백그라운드 추적 대상용. 자기 Sector 매핑 + 방 클램프로 그려지고,
    // 현재 섹터 게이트 전환 보간은 따르지 않는다(다른 섹터에서도 자기 위치에 머묾).
    public Marker AddMarker(IMinimapTracked source, Color color, float worldSize = 30f, Sprite sprite = null, bool rotateWithTarget = false)
        => source == null ? null
            : CreateMarker(
                source,
                new MinimapMarkerSettings(sprite, color, worldSize, rotateWithTarget: rotateWithTarget),
                followsTransition: false,
                renderOnTop: false);

    public Marker AddMarker(IMinimapTracked source, MinimapMarkerSettings marker)
        => source == null ? null
            : CreateMarker(source, marker, followsTransition: false, renderOnTop: false);

    public Marker AddEliteMarker(IMinimapTracked source, Sprite sprite)
    {
        if (source == null) return null;
        MinimapMarkerSettings marker = source.Faction == NavFaction.Ally ? allyEliteMarker : enemyEliteMarker;
        return CreateMarker(source, marker.WithSprite(sprite), followsTransition: false, renderOnTop: false);
    }

    // worldSize: 월드 단위 마커 크기. 방 위치 매핑과 같은 스케일로 변환 후 화면 px로 clamp → 맵과 함께 스케일.
    private Marker CreateMarker(IMinimapTracked source, MinimapMarkerSettings markerSettings, bool followsTransition, bool renderOnTop)
    {
        if (_image == null) return null;

        float px = Mathf.Clamp(
            markerSettings.WorldSize * MarkerWorldToImagePx(),
            markerSettings.MinScreenPx,
            markerSettings.MaxScreenPx);

        var go = new GameObject("Marker", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(_image.rectTransform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f); // 이미지 좌하단 기준 픽셀 좌표
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(px, px);

        var img = go.AddComponent<Image>();
        Sprite sprite = markerSettings.Sprite;
        img.sprite = sprite;
        img.preserveAspect = sprite != null;
        img.useSpriteMesh = sprite != null;
        img.color = markerSettings.Color;
        img.raycastTarget = false;
        RectTransform directionRect = null;

        if (markerSettings.RotateWithTarget && playerDirectionSprite != null)
        {
            var dirGo = new GameObject("Direction", typeof(RectTransform));
            directionRect = dirGo.GetComponent<RectTransform>();
            directionRect.SetParent(rt, false);
            directionRect.anchorMin = directionRect.anchorMax = new Vector2(0.5f, 0.5f);
            directionRect.pivot = new Vector2(0.5f, 0.5f);
            directionRect.sizeDelta = new Vector2(px * playerDirectionSizeRatio.x, px * playerDirectionSizeRatio.y);
            directionRect.anchoredPosition = Vector2.up * px * playerDirectionOffsetRatio;

            var dirImg = dirGo.AddComponent<Image>();
            dirImg.sprite = playerDirectionSprite;
            dirImg.preserveAspect = true;
            dirImg.color = playerDirectionColor;
            dirImg.raycastTarget = false;
        }

        var marker = new Marker
        {
            Source = source,
            Rect = rt,
            Image = img,
            DirectionRect = directionRect,
            RotateWithTarget = markerSettings.RotateWithTarget,
            FollowsCurrentSectorTransition = followsTransition,
            RenderOnTop = renderOnTop,
        };
        _markers.Add(marker);
        return marker;
    }

    public void RemoveMarker(Marker marker)
    {
        if (marker == null || !_markers.Remove(marker)) return;
        if (marker.Rect != null) Destroy(marker.Rect.gameObject);
    }

    // 마커/오버레이 크기를 방 위치 매핑과 같은 스케일로(월드 → 이미지 px). sectorVisualScale 포함.
    private float MarkerWorldToImagePx()
        => _worldToImagePx * Mathf.Max(sectorVisualScale, 0.0001f);

    // 표현:
    //  · 게이트/필드 이동 중 → 개별 마커 대신 경로 레인 위 방향 배지로 집계.
    //  · 그 외(현재·배경 섹터) → 엘리트 개별 마커를 실제 위치로(배경 섹터에서도 움직인다).
    //  · 잡몹 병력·점령 게이지%는 RenderBadges가 SectorBattleManager에서 읽어 섹터별로 그린다.
    private void UpdateMarkers()
    {
        if (_image == null) return;

        Sector active = SectorManager.Instance != null ? SectorManager.Instance.CurrentSector : null;
        _routeCounts.Clear();

        for (int i = 0; i < _markers.Count; i++)
        {
            Marker marker = _markers[i];
            if (marker.Source == null)
            {
                if (marker.Rect != null) marker.Rect.gameObject.SetActive(false);
                continue;
            }

            if (ShouldHideEliteMarker(marker.Source, active)
                && !TryResolveTransition(marker, out _, out _, out _))
            {
                marker.Rect.gameObject.SetActive(false);
                marker.Placed = false;
                continue;
            }

            // 이동 중 → 경로 레인 방향 배지.
            if (TryResolveTransition(marker, out Sector from, out Sector to, out float t))
            {
                AccumulateRoute(from, to, marker.Source.Faction);
                marker.Rect.gameObject.SetActive(false);
                marker.Placed = false;
                continue;
            }

            // 현재 섹터·플레이어·배경 섹터 모두 실제 위치로 표시한다(엘리트는 배경 섹터에서도 움직인다).
            // 잡몹 병력/점령은 개별 마커가 아니라 RenderBadges가 SectorBattleManager에서 읽어 그린다.
            ShowMarker(marker, MarkerPositionPx(marker.Source, active), ForwardToUiRotation(marker.Source.Forward));
        }

        RenderStrategicLinks();
        RenderBadges();
        RenderRouteIndicators();
        BringTopMarkersToFront();
    }

    // 같은 링크에 속한 점령지 사이의 게이트만 진영색으로 강조하고, 링크 허브에 전체 힘을 표시한다.
    private void RenderStrategicLinks()
    {
        SectorBattleManager battle = SectorBattleManager.Instance;
        int usedLines = 0;
        int usedFrames = 0;

        if (battle != null)
        {
            for (int i = 0; i < _model.Edges.Count; i++)
            {
                MinimapModel.Edge edge = _model.Edges[i];
                if (edge.A < 0 || edge.B < 0 || edge.A >= _model.Nodes.Count || edge.B >= _model.Nodes.Count)
                    continue;
                if (!battle.TryGetState(_model.Nodes[edge.A].Sector, out SectorBattleState a)
                    || !battle.TryGetState(_model.Nodes[edge.B].Sector, out SectorBattleState b))
                    continue;
                if (a.LinkId <= 0 || a.LinkId != b.LinkId || a.Control != b.Control)
                    continue;

                Vector2 from = CellToImageLocal(AnchorCenterCell(_model.Nodes[edge.A]));
                Vector2 to = CellToImageLocal(AnchorCenterCell(_model.Nodes[edge.B]));
                Color color = a.Control == SectorControl.Ally ? linkAllyColor : linkEnemyColor;
                SetLinkLine(GetLinkLine(usedLines++), from, to, color);
            }

            if (hubFrameSprite != null)
            {
                foreach (KeyValuePair<Sector, SectorBattleState> kv in battle.States)
                {
                    SectorBattleState state = kv.Value;
                    if (!state.IsLinkHub || state.LinkInfluence < 2) continue;

                    int idx = IndexOfSector(state.Sector);
                    if (idx < 0) continue;
                    SetHubFrame(GetHubFrame(usedFrames++), idx);
                }
            }

        }

        for (int i = usedLines; i < _linkLinePool.Count; i++)
            _linkLinePool[i].Rect.gameObject.SetActive(false);
        for (int i = usedFrames; i < _hubFramePool.Count; i++)
            _hubFramePool[i].gameObject.SetActive(false);
    }

    private LinkLine GetLinkLine(int index)
    {
        while (_linkLinePool.Count <= index)
        {
            var go = new GameObject("StrategicLink", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_image.rectTransform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            _linkLinePool.Add(new LinkLine { Rect = rt, Image = img });
        }
        return _linkLinePool[index];
    }

    private void SetLinkLine(LinkLine line, Vector2 from, Vector2 to, Color color)
    {
        Vector2 delta = to - from;
        line.Rect.gameObject.SetActive(true);
        line.Rect.anchoredPosition = (from + to) * 0.5f;
        line.Rect.sizeDelta = new Vector2(delta.magnitude, Mathf.Max(1f, linkLineWidthPx));
        line.Rect.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        line.Rect.SetAsFirstSibling();
        line.Image.color = color;
    }

    private Image GetHubFrame(int index)
    {
        while (_hubFramePool.Count <= index)
        {
            var go = new GameObject("LinkHubFrame", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_image.rectTransform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var image = go.AddComponent<Image>();
            image.sprite = hubFrameSprite;
            image.raycastTarget = false;
            _hubFramePool.Add(image);
        }
        return _hubFramePool[index];
    }

    private void SetHubFrame(Image frame, int nodeIndex)
    {
        Image room = FindRoomImage(nodeIndex);
        if (room == null)
        {
            frame.gameObject.SetActive(false);
            return;
        }

        RectTransform roomRect = room.rectTransform;
        RectTransform rt = frame.rectTransform;
        frame.gameObject.SetActive(true);
        frame.sprite = hubFrameSprite;
        frame.color = Color.white;
        ResolveVisibleRoomRect(room, out Vector2 visibleCenterOffset, out Vector2 visibleSize);
        rt.anchoredPosition = roomRect.anchoredPosition + RotateUiOffset(visibleCenterOffset, roomRect.localEulerAngles.z);
        float frameSize = Mathf.Max(visibleSize.x, visibleSize.y);
        rt.sizeDelta = Vector2.one * frameSize;
        rt.localEulerAngles = Vector3.zero;
        rt.SetAsLastSibling();
    }

    // 방 스프라이트의 투명 여백을 제외한 tight mesh 영역을 UI rect 좌표로 환산한다.
    private static void ResolveVisibleRoomRect(Image room, out Vector2 centerOffset, out Vector2 size)
    {
        RectTransform rt = room.rectTransform;
        Sprite sprite = room.sprite;
        if (sprite == null || sprite.vertices == null || sprite.vertices.Length == 0)
        {
            centerOffset = Vector2.zero;
            size = rt.sizeDelta;
            return;
        }

        Vector2 min = sprite.vertices[0];
        Vector2 max = min;
        for (int i = 1; i < sprite.vertices.Length; i++)
        {
            min = Vector2.Min(min, sprite.vertices[i]);
            max = Vector2.Max(max, sprite.vertices[i]);
        }

        Vector2 fullSpriteSize = sprite.rect.size / Mathf.Max(0.0001f, sprite.pixelsPerUnit);
        Vector2 tightSize = max - min;
        Vector2 tightCenter = (min + max) * 0.5f;
        Vector2 scale = new Vector2(
            rt.sizeDelta.x / Mathf.Max(0.0001f, fullSpriteSize.x),
            rt.sizeDelta.y / Mathf.Max(0.0001f, fullSpriteSize.y));

        centerOffset = Vector2.Scale(tightCenter, scale);
        size = Vector2.Scale(tightSize, scale);
    }

    private static Vector2 RotateUiOffset(Vector2 offset, float degrees)
        => Quaternion.Euler(0f, 0f, degrees) * offset;

    private Image FindRoomImage(int nodeIndex)
    {
        for (int i = 0; i < _rooms.Count; i++)
            if (_rooms[i].index == nodeIndex)
                return _rooms[i].image;
        return null;
    }

    private static bool ShouldHideEliteMarker(IMinimapTracked source, Sector active)
    {
        if (source is not Elite_State elite)
            return false;

        return elite.IsInTransit
               || (active != null && elite.CurrentSector == active && elite.Embodiment == null);
    }

    private void BringTopMarkersToFront()
    {
        for (int i = 0; i < _markers.Count; i++)
        {
            Marker marker = _markers[i];
            if (!marker.RenderOnTop || marker.Rect == null || !marker.Rect.gameObject.activeSelf)
                continue;

            marker.Rect.SetAsLastSibling();
        }
    }

    private void ShowMarker(Marker marker, Vector2 target, float rotation)
    {
        marker.Rect.gameObject.SetActive(true);

        if (!marker.Placed)
        {
            marker.Rect.anchoredPosition = target;
            marker.Placed = true;
        }
        else
        {
            float k = markerSmoothing > 0f ? 1f - Mathf.Exp(-markerSmoothing * Time.unscaledDeltaTime) : 1f;
            marker.Rect.anchoredPosition = Vector2.Lerp(marker.Rect.anchoredPosition, target, k);
        }

        if (marker.RotateWithTarget)
        {
            RectTransform rotateTarget = marker.DirectionRect != null ? marker.DirectionRect : marker.Rect;
            rotateTarget.localEulerAngles = new Vector3(0f, 0f, rotation);
            if (marker.DirectionRect != null)
                marker.DirectionRect.anchoredPosition = DirectionOffset(rotation, marker.Rect.rect.width * playerDirectionOffsetRatio);
        }
    }

    private static Vector2 DirectionOffset(float uiRotation, float distance)
        => (Vector2)(Quaternion.Euler(0f, 0f, uiRotation) * Vector2.up) * distance;

    // 플레이어(전역 게이트 전환) 또는 장수(필드 이동)의 통로 이동 상태를 출발/도착 섹터·진행도로 환산.
    private bool TryResolveTransition(Marker marker, out Sector from, out Sector to, out float t)
    {
        if (marker.FollowsCurrentSectorTransition && IsGateTransitioning()
            && _transitionFromIndex >= 0 && _transitionToIndex >= 0)
        {
            from = _model.Nodes[_transitionFromIndex].Sector;
            to   = _model.Nodes[_transitionToIndex].Sector;
            t    = 0f;
            return true;
        }

        if (marker.Source.TryGetTransition(out from, out to, out t) && to != null)
            return true;

        from = null; to = null; t = 0f;
        return false;
    }

    private bool AccumulateRoute(Sector from, Sector to, NavFaction faction)
    {
        int fi = IndexOfSector(from);
        int ti = IndexOfSector(to);
        if (fi < 0 || ti < 0 || fi == ti) return false;

        long key = DirectedRouteKey(fi, ti);
        _routeCounts.TryGetValue(key, out RouteCounts counts);
        if (faction == NavFaction.Ally) counts.Ally++;
        else counts.Enemy++;
        _routeCounts[key] = counts;
        return true;
    }

    private void RenderRouteIndicators()
    {
        int used = 0;
        foreach (KeyValuePair<long, RouteCounts> kv in _routeCounts)
        {
            int from = RouteFrom(kv.Key);
            int to = RouteTo(kv.Key);
            if (from < 0 || to < 0 || from >= _model.Nodes.Count || to >= _model.Nodes.Count) continue;

            Vector2 a = CellToImageLocal(AnchorCenterCell(_model.Nodes[from]));
            Vector2 b = CellToImageLocal(AnchorCenterCell(_model.Nodes[to]));
            Vector2 dir = b - a;
            if (dir.sqrMagnitude <= 0.0001f) continue;
            dir.Normalize();

            Vector2 lane = RouteLaneNormal(dir);
            Vector2 center = (a + b) * 0.5f + lane * routeLaneOffsetPx;
            Vector2 badgeBase = lane * routeBadgeOffsetPx;

            RouteIndicator indicator = GetRouteIndicator(used++);
            indicator.Root.gameObject.SetActive(true);
            indicator.Root.anchoredPosition = center;
            indicator.ArrowRect.localEulerAngles = new Vector3(0f, 0f, DirectionToUiRotation(dir));

            bool hasAlly = kv.Value.Ally > 0;
            bool hasEnemy = kv.Value.Enemy > 0;
            float spread = hasAlly && hasEnemy ? routeBadgeSpacingPx * 0.5f : 0f;
            bool verticalRoute = Mathf.Abs(dir.y) > Mathf.Abs(dir.x);
            SetRouteBadge(indicator.AllyBadge, hasAlly, kv.Value.Ally, badgeAllyColor, badgeBase - dir * spread, verticalRoute);
            SetRouteBadge(indicator.EnemyBadge, hasEnemy, kv.Value.Enemy, badgeEnemyColor, badgeBase + dir * spread, verticalRoute);
        }

        for (int i = used; i < _routePool.Count; i++)
            _routePool[i].Root.gameObject.SetActive(false);
    }

    private RouteIndicator GetRouteIndicator(int index)
    {
        while (_routePool.Count <= index)
        {
            var rootGo = new GameObject("RouteIndicator", typeof(RectTransform));
            var root = rootGo.GetComponent<RectTransform>();
            root.SetParent(_image.rectTransform, false);
            root.anchorMin = root.anchorMax = new Vector2(0f, 0f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(72f, 52f);

            var indicator = new RouteIndicator
            {
                Root = root,
                Arrow = CreateRouteArrow(root),
                AllyBadge = CreateRouteText(root, "AllyBadge", routeBadgeFontSize, badgeAllyColor, new Vector2(24f, 20f)),
                EnemyBadge = CreateRouteText(root, "EnemyBadge", routeBadgeFontSize, badgeEnemyColor, new Vector2(24f, 20f)),
            };
            indicator.ArrowRect = indicator.Arrow.rectTransform;
            _routePool.Add(indicator);
        }

        return _routePool[index];
    }

    private Graphic CreateRouteArrow(RectTransform parent)
    {
        var go = new GameObject("Arrow", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = routeArrowSize;

        if (routeArrowSprite != null)
        {
            var img = go.AddComponent<Image>();
            img.sprite = routeArrowSprite;
            img.preserveAspect = true;
            img.color = routeArrowColor;
            img.raycastTarget = false;
            return img;
        }

        var txt = go.AddComponent<Text>();
        txt.font = BadgeFont();
        txt.fontSize = Mathf.RoundToInt(routeArrowSize.y);
        txt.fontStyle = FontStyle.Bold;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.text = "^";
        txt.color = routeArrowColor;
        txt.raycastTarget = false;
        return txt;
    }

    private Text CreateRouteText(RectTransform parent, string name, int fontSize, Color color, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;

        var txt = go.AddComponent<Text>();
        txt.font = BadgeFont();
        txt.fontSize = fontSize;
        txt.fontStyle = FontStyle.Bold;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.lineSpacing = 0.55f;
        txt.color = color;
        txt.raycastTarget = false;
        return txt;
    }

    private void SetRouteBadge(Text badge, bool visible, int count, Color color, Vector2 pos, bool vertical)
    {
        badge.gameObject.SetActive(visible);
        if (!visible) return;

        badge.text = RouteBadgeText(count, vertical);
        badge.fontSize = BadgeFontSize(count, routeBadgeFontSize);
        badge.color = color;
        badge.rectTransform.anchoredPosition = pos;
    }

    private static Vector2 RouteLaneNormal(Vector2 dir)
    {
        if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.y))
            return dir.x >= 0f ? Vector2.down : Vector2.up;

        return dir.y >= 0f ? Vector2.right : Vector2.left;
    }

    // 섹터마다 잡몹 병력(진영별 숫자 배지, 좌=아군/우=적)과 점령 게이지%(중심 아래)를 그린다.
    // 잡몹 병력·점령의 진실은 SectorBattleManager. 플레이어가 있는 섹터에도 점령 상태를 표시한다.
    private void RenderBadges()
    {
        int used = 0;
        SectorBattleManager battle = SectorBattleManager.Instance;
        if (battle != null)
        {
            foreach (KeyValuePair<Sector, SectorBattleState> kv in battle.States)
            {
                SectorBattleState state = kv.Value;
                int idx = IndexOfSector(state.Sector);
                if (idx < 0) continue;

                Vector2 center = CellToImageLocal(AnchorCenterCell(_model.Nodes[idx]));
                int ally  = Mathf.RoundToInt(state.AllyTotal);  // 총 병력 = 예비 + 화면.
                int enemy = Mathf.RoundToInt(state.EnemyTotal);
                bool both = ally > 0 && enemy > 0;

                if (showSectorMobCounts)
                {
                    if (ally > 0)
                        used = SetBadge(used, center + new Vector2(both ? -badgeOffsetPx : 0f, badgeOffsetPx), ally, badgeAllyColor);
                    if (enemy > 0)
                        used = SetBadge(used, center + new Vector2(both ? badgeOffsetPx : 0f, badgeOffsetPx), enemy, badgeEnemyColor);
                }

                // 점령 게이지%: 0=적 완전 점령(빨강) ~ 100=아군 완전 점령(파랑). 병력 있는 섹터만.
                if (both || ally > 0 || enemy > 0)
                {
                    float n = state.GaugeNormalized;
                    Color gaugeColor = Color.Lerp(gaugeEnemyColor, gaugeAllyColor, n);
                    used = SetBadgeText(used, center - new Vector2(0f, gaugeOffsetPx),
                        $"{Mathf.FloorToInt(n * 100f)}%", gaugeColor, gaugeFontSize);
                }
            }
        }

        for (int i = used; i < _badgePool.Count; i++)
            _badgePool[i].gameObject.SetActive(false);
    }

    private int SetBadge(int poolIndex, Vector2 pos, int count, Color color)
        => SetBadgeText(poolIndex, pos, BadgeText(count), color, BadgeFontSize(count, badgeFontSize));

    private int SetBadgeText(int poolIndex, Vector2 pos, string text, Color color, int fontSize)
    {
        Text label = GetBadge(poolIndex);
        label.gameObject.SetActive(true);
        label.text  = text;
        label.fontSize = fontSize;
        label.color = color;
        label.rectTransform.anchoredPosition = pos;
        return poolIndex + 1;
    }

    private Text GetBadge(int index)
    {
        while (_badgePool.Count <= index)
        {
            var go = new GameObject("Badge", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_image.rectTransform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f); // 이미지 좌하단 기준 픽셀 좌표
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(28f, 22f);

            var txt = go.AddComponent<Text>();
            txt.font               = BadgeFont();
            txt.fontSize           = badgeFontSize;
            txt.fontStyle          = FontStyle.Bold;
            txt.alignment          = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow   = VerticalWrapMode.Overflow;
            txt.raycastTarget      = false;
            _badgePool.Add(txt);
        }
        return _badgePool[index];
    }

    private static string BadgeText(int count)
        => count <= 1 ? "•" : count.ToString();

    private static string RouteBadgeText(int count, bool vertical)
    {
        if (count <= 0) return string.Empty;
        if (!vertical) return new string('•', count);

        var text = new System.Text.StringBuilder(count * 2 - 1);
        for (int i = 0; i < count; i++)
        {
            if (i > 0) text.Append('\n');
            text.Append('•');
        }
        return text.ToString();
    }

    private static int BadgeFontSize(int count, int baseSize)
        => count <= 1 ? Mathf.RoundToInt(baseSize * 0.9f) : baseSize;

    private static Font BadgeFont()
    {
        if (_badgeFont == null)
            _badgeFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Arial", "Helvetica", "Liberation Sans", "DejaVu Sans" }, 16);
        return _badgeFont;
    }

    // 마커 전용 전역 투영: 월드 좌표를 셀(1배)로 변환해 이미지 px로 매핑한다. 섹터별 재투영/클램프가 없어
    // 전 구간 연속이라 마커가 점프 없이 부드럽게 이동한다. 섹터 transform.position=GridToWorld(anchor)라
    // 월드와 그리드가 비례하므로 위치는 방 그리드에 정렬된다(방 시각 크기 2배와는 별개 — 마커는 안쪽에서 움직임).
    private Vector2 MarkerPositionPx(IMinimapTracked source, Sector active)
    {
        Sector sector = source.Sector != null ? source.Sector : active;
        int index = IndexOfSector(sector);
        if (index >= 0)
            return WorldToSectorMarkerPx(source.WorldPosition, _model.Nodes[index]);

        return WorldToMarkerPx(source.WorldPosition);
    }

    private Vector2 WorldToSectorMarkerPx(Vector3 world, MinimapModel.Node node)
    {
        if (node == null || node.Sector == null)
            return WorldToMarkerPx(world);

        Vector3 local = node.Sector.transform.InverseTransformPoint(world);
        Vector2 rotatedLocal = RotateLocal(new Vector2(local.x, local.z), node.RotationSteps);
        float scale = Mathf.Max(_cellSize, 0.0001f);
        Vector2 cell = AnchorCenterCell(node) + rotatedLocal * (Mathf.Max(sectorVisualScale, 0.0001f) / scale);
        return CellToImageLocal(cell);
    }

    private Vector2 WorldToMarkerPx(Vector3 world)
    {
        float scale = Mathf.Max(_cellSize, 0.0001f);
        return CellToImageLocal(new Vector2(world.x / scale + 0.5f, world.z / scale + 0.5f));
    }

    private static float ForwardToUiRotation(Vector3 forward)
        => DirectionToUiRotation(new Vector2(forward.x, forward.z));

    // 평면 방향(+x=동, +y=북/+z) → UI z회전. 마커가 그 방향을 향하게.
    private static float DirectionToUiRotation(Vector2 dir)
    {
        if (dir.sqrMagnitude <= 0.0001f) return 0f;
        dir.Normalize();
        return -Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
    }

    private Vector2 CellToImageLocal(Vector2 cell)
    {
        float px = (cell.x - _mapMinCell.x) * TexturePixelsPerCell;
        float py = (cell.y - _mapMinCell.y) * TexturePixelsPerCell;

        float lx = Mathf.Clamp(px * _renderScale, 0f, _texWidth  * _renderScale);
        float ly = Mathf.Clamp(py * _renderScale, 0f, _texHeight * _renderScale);
        return new Vector2(lx, ly);
    }

    // ── 현재/방문 상태 동기화 ──────────────────────────────────────────────────

    private void SyncCurrentSector()
    {
        Sector current = SectorManager.Instance != null ? SectorManager.Instance.CurrentSector : null;
        int index = IndexOfSector(current);
        if (index == _currentIndex) return;

        int previousIndex = _currentIndex;
        _currentIndex = index;
        if (index >= 0) _visited.Add(index);
        if (previousIndex >= 0 && index >= 0 && HasEdge(previousIndex, index))
        {
            _transitionEdgeKey   = EdgeKey(previousIndex, index);
            _transitionFromIndex = previousIndex;
            _transitionToIndex   = index;
        }
        _dirty = true;
    }

    private void SyncTransitionState()
    {
        bool isTransitioning = IsGateTransitioning();
        if (isTransitioning == _wasTransitioning) return;

        _wasTransitioning = isTransitioning;
        if (!isTransitioning)
        {
            _transitionEdgeKey   = null;
            _transitionFromIndex = -1;
            _transitionToIndex   = -1;
        }
        _dirty = true;
    }

    private bool HasEdge(int a, int b)
    {
        for (int i = 0; i < _model.Edges.Count; i++)
        {
            MinimapModel.Edge edge = _model.Edges[i];
            if ((edge.A == a && edge.B == b) || (edge.A == b && edge.B == a))
                return true;
        }
        return false;
    }

    private int IndexOfSector(Sector sector)
    {
        if (sector == null) return -1;
        for (int i = 0; i < _model.Nodes.Count; i++)
            if (_model.Nodes[i].Sector == sector) return i;
        return -1;
    }

    // ── 캔버스 / 텍스처 구성 ───────────────────────────────────────────────────

    private void BuildCanvas()
    {
        _rootRect = minimapRoot != null ? minimapRoot : transform as RectTransform;
        _usesHudLayout = _rootRect != null && _rootRect.GetComponentInParent<Canvas>() != null;

        if (!_usesHudLayout)
        {
            var canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            if (GetComponent<CanvasScaler>() == null)
                gameObject.AddComponent<CanvasScaler>();
        }

        var imageGo = new GameObject("MapImage", typeof(RectTransform));
        imageGo.transform.SetParent(_rootRect != null ? _rootRect : transform, false);
        _image = imageGo.AddComponent<RawImage>();

        var rt = _image.rectTransform;
        if (_usesHudLayout)
        {
            Vector2 pivot = _rootRect != null ? _rootRect.pivot : new Vector2(0.5f, 0.5f);
            rt.anchorMin = rt.anchorMax = rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero;
        }
        else
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-StandaloneScreenMargin.x, -StandaloneScreenMargin.y);
        }
    }

    private void BuildTexture()
    {
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        foreach (MinimapModel.Node node in _model.Nodes)
        {
            foreach (Vector2Int cell in node.Cells)
            {
                Vector2 center = new Vector2(cell.x + 0.5f, cell.y + 0.5f);
                min = Vector2.Min(min, center - Vector2.one * 0.5f);
                max = Vector2.Max(max, center + Vector2.one * 0.5f);
            }
        }

        min -= Vector2.one * boundsPaddingCells;
        max += Vector2.one * boundsPaddingCells;

        _mapMinCell = min;
        _texWidth  = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) * TexturePixelsPerCell));
        _texHeight = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) * TexturePixelsPerCell));

        _texture = new Texture2D(_texWidth, _texHeight, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode   = TextureWrapMode.Clamp,
        };
        _buffer = new Color32[_texWidth * _texHeight];
        _image.texture = _texture;

        // 종횡비 유지하며 패널 박스 안에 맞춤
        if (_usesHudLayout)
        {
            Vector2 available = _rootRect != null ? _rootRect.rect.size : Vector2.zero;
            if (available.x <= 0f || available.y <= 0f)
                available = new Vector2(FallbackPanelSize, FallbackPanelSize);

            _renderScale = Mathf.Min(available.x / Mathf.Max(1, _texWidth),
                                     available.y / Mathf.Max(1, _texHeight));
        }
        else
        {
            _renderScale = (float)FallbackPanelSize / Mathf.Max(_texWidth, _texHeight);
        }
        _image.rectTransform.sizeDelta = new Vector2(_texWidth * _renderScale, _texHeight * _renderScale);

        // 월드→이미지 px: 마커와 방 크기가 같은 스케일을 쓰도록 한 곳에서 산출
        _worldToImagePx = _cellSize > 0f
            ? TexturePixelsPerCell * _renderScale / _cellSize
            : TexturePixelsPerCell * _renderScale;
    }

    // ── 정적 맵(배경 + 통로) — 1회 래스터화 ──────────────────────────────────────

    private void DrawStaticMap()
    {
        FillAll(backgroundColor);

        foreach (MinimapModel.Edge edge in _model.Edges)
        {
            long edgeKey = EdgeKey(edge.A, edge.B);
            Color color = IsGateTransitioning() && _transitionEdgeKey.HasValue && _transitionEdgeKey.Value == edgeKey
                ? TransitionColor
                : corridorColor;
            DrawCorridor(_model.Nodes[edge.A].AnchorCell, _model.Nodes[edge.B].AnchorCell, color);
        }

        _texture.SetPixels32(_buffer);
        _texture.Apply(false);
    }

    private Color ResolveColor(MinimapModel.Node node)
    {
        if (IsGateTransitioning() && node.Index == _currentIndex) return TransitionColor;
        if (node.Index == _currentIndex)   return FallbackCurrentColor;
        // TODO: 방문/미방문 섹터 색 구분은 임시 비활성화.
        // if (_visited.Contains(node.Index)) return FallbackVisitedColor;
        if (node.IsStart)                  return FallbackStartColor;
        return FallbackRoomColor;
    }

    // 1칸 이웃끼리만 연결되므로 통로는 항상 축 정렬(수평/수직) 직사각형이다.
    private void DrawCorridor(Vector2Int cellA, Vector2Int cellB, Color color)
    {
        Vector2 a    = CellCenterPx(cellA);
        Vector2 b    = CellCenterPx(cellB);
        float   half = corridorThickness * 0.5f;

        FillRect(Mathf.Min(a.x, b.x) - half, Mathf.Min(a.y, b.y) - half,
                 Mathf.Max(a.x, b.x) + half, Mathf.Max(a.y, b.y) + half, color);
    }

    private static long EdgeKey(int a, int b)
    {
        int min = Mathf.Min(a, b);
        int max = Mathf.Max(a, b);
        return ((long)min << 32) | (uint)max;
    }

    private static long DirectedRouteKey(int from, int to)
        => ((long)from << 32) | (uint)to;

    private static int RouteFrom(long key)
        => (int)(key >> 32);

    private static int RouteTo(long key)
        => (int)(key & 0xffffffff);

    private static bool IsGateTransitioning()
        => SectorManager.Instance != null && SectorManager.Instance.IsTransitioning;

    // 셀 → 텍스처 픽셀(블록 중앙). +y(북)이 위로 가도록 텍스처 y 그대로 사용.
    private Vector2 CellCenterPx(Vector2Int cell)
        => (new Vector2(cell.x + 0.5f, cell.y + 0.5f) - _mapMinCell) * TexturePixelsPerCell;

    private static Vector2 AnchorCenterCell(MinimapModel.Node node)
        => new Vector2(node.AnchorCell.x + 0.5f, node.AnchorCell.y + 0.5f);

    private void FillAll(Color color)
    {
        Color32 c = color;
        for (int i = 0; i < _buffer.Length; i++) _buffer[i] = c;
    }

    private void FillRect(float x0, float y0, float x1, float y1, Color color)
    {
        int xMin = Mathf.Clamp(Mathf.RoundToInt(x0), 0, _texWidth);
        int yMin = Mathf.Clamp(Mathf.RoundToInt(y0), 0, _texHeight);
        int xMax = Mathf.Clamp(Mathf.RoundToInt(x1), 0, _texWidth);
        int yMax = Mathf.Clamp(Mathf.RoundToInt(y1), 0, _texHeight);

        Color32 c = color;
        for (int y = yMin; y < yMax; y++)
        {
            int row = y * _texWidth;
            for (int x = xMin; x < xMax; x++)
                _buffer[row + x] = c;
        }
    }

    private void OnDestroy()
    {
        Clear();
    }

    public void Clear()
    {
        Transform contentRoot = _rootRect != null
            ? _rootRect
            : (minimapRoot != null ? minimapRoot : transform);

        _model = null;
        _image = null;
        _buffer = null;
        _rootRect = null;
        _cellSize = 0f;
        _renderScale = 0f;
        _worldToImagePx = 0f;
        _visited.Clear();
        _transitionEdgeKey = null;
        _transitionFromIndex = -1;
        _transitionToIndex = -1;
        _rooms.Clear();
        _markers.Clear();
        _badgePool.Clear();   // 배지 GameObject는 MapImage 자식이라 아래에서 함께 파괴된다.
        _routeCounts.Clear();
        _routePool.Clear();   // 이동 표시 GameObject도 MapImage 자식이라 아래에서 함께 파괴된다.
        _linkLinePool.Clear();
        _hubFramePool.Clear();
        _currentIndex = -1;
        _wasTransitioning = false;
        _dirty = false;

        if (_texture != null)
        {
            Destroy(_texture);
            _texture = null;
        }

        Transform t = contentRoot != null ? contentRoot : transform;
        for (int i = t.childCount - 1; i >= 0; i--)
        {
            Transform child = t.GetChild(i);
            if (child.name == "MapImage")
                Destroy(child.gameObject);
        }
    }
}
