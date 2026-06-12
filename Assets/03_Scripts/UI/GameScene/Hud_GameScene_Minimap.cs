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
    private static readonly Color FallbackCurrentColor = new(1.00f, 0.78f, 0.18f, 1f);
    private static readonly Color FallbackStartColor = new(0.30f, 0.78f, 0.42f, 1f);
    private static readonly Color TransitionColor = new(0.26f, 0.86f, 0.78f, 1f);

    [Header("Layout")]
    [SerializeField] private RectTransform minimapRoot;
    [SerializeField] private float   sectorVisualScale = 2f;
    [SerializeField] private float   boundsPaddingCells = 0.25f;
    [SerializeField] private Color   backgroundColor = new(0.05f, 0.06f, 0.09f, 0.85f);

    [Header("Outer frame")]
    [SerializeField] private bool showOuterFrame = true;
    [Tooltip("바깥 테두리에 쓸 9-slice 프레임 스프라이트. 지정하면 코너 선분 대신 이 이미지를 미니맵 영역에 맞춰 " +
             "늘려 그린다(코너 보존). Sprite Editor에서 border(상/하/좌/우)를 지정해야 9-slice가 적용된다. 비우면 코너 선분 폴백.")]
    [SerializeField] private Sprite outerFrameSprite;
    [Tooltip("프레임 스프라이트 9-slice의 가운데(센터)를 채울지. 보통 false라야 맵이 비친다.")]
    [SerializeField] private bool outerFrameFillCenter;
    [SerializeField] private Color outerFrameColor = new(0.08f, 0.78f, 1f, 0.85f);
    [SerializeField] private float outerFramePaddingPx = 10f;
    [SerializeField] private float outerFrameThicknessPx = 2f;
    [SerializeField] private float outerFrameCornerLengthPx = 24f;

    [Header("Markers")]
    [SerializeField] private float markerSmoothing = 12f; // 목표로 수렴하는 속도(작을수록 러프, 0이면 즉시)
    [SerializeField] private MinimapMarkerSettings playerMarker = MinimapMarkerSettings.DefaultPlayer;
    [SerializeField] private MinimapMarkerSettings allyEliteMarker = new(null, new Color(0f, 0.31f, 1f, 1f), 15f, 8f, 30f, false);
    [SerializeField] private MinimapMarkerSettings enemyEliteMarker = new(null, new Color(1f, 0.06f, 0f, 1f), 15f, 8f, 30f, false);
    // 본진(적 코어) 마커 — 결전 목표를 항상 표시. 어느 섹터에 있든 그 노드에 고정·렌더 최상단. sprite 비우면 금색 마커.
    [SerializeField] private MinimapMarkerSettings capitalMarker = new(null, new Color(1f, 0.84f, 0.1f, 1f), 50f, 18f, 64f, false);
    // 링크 허브(영향력 큰 점령지) 마커 — 허브 섹터 중심에 고정. capital과 동일 방식(sprite 비우면 색 마커).
    [SerializeField] private MinimapMarkerSettings hubMarker = new(null, new Color(1f, 1f, 1f, 0.9f), 40f, 12f, 50f, false);

    [Header("Player direction arrow")]
    [SerializeField] private Sprite playerDirectionSprite;
    [SerializeField] private Vector2 playerDirectionSizeRatio = new(0.78f, 0.62f);
    [SerializeField] private float playerDirectionOffsetRatio = 0.42f;
    [SerializeField] private Color playerDirectionColor = Color.black;

    [Header("Gate routes")]
    [Tooltip("중립(양쪽 점령 진영 불일치/미점령) 게이트 라우트 색.")]
    [SerializeField] private Color gateRouteColor = new(0.85f, 0.9f, 1f, 1f);
    [Tooltip("양쪽 섹터가 모두 아군 점령일 때 색.")]
    [SerializeField] private Color gateRouteAllyColor = new(0.30f, 0.60f, 1f, 1f);
    [Tooltip("양쪽 섹터가 모두 적 점령일 때 색.")]
    [SerializeField] private Color gateRouteEnemyColor = new(1f, 0.35f, 0.35f, 1f);
    [SerializeField] private float gateRouteWidthPx = 3f;

    [Header("Background sector badge")]
    [SerializeField] private bool showSectorMobCounts = true;
    [SerializeField] private Color badgeAllyColor  = new(0.35f, 1f, 0.45f, 1f);
    [SerializeField] private Color badgeEnemyColor = new(1f, 0.4f, 0.4f, 1f);
    [SerializeField] private int   badgeFontSize   = 16;
    [SerializeField] private float badgeOffsetPx   = 9f; // 아군/적 숫자를 노드 중심 좌우로 벌리는 간격

    [Header("Sector control gauge")]
    [SerializeField] private bool showSectorControlPercent = true;
    [SerializeField] private Color gaugeAllyColor  = new(0.30f, 0.60f, 1f, 1f);   // 아군 우세 → 파랑
    [SerializeField] private Color gaugeEnemyColor = new(1f, 0.35f, 0.35f, 1f);   // 적 우세 → 빨강
    [SerializeField] private int   gaugeFontSize   = 14;
    [SerializeField] private float gaugeOffsetPx   = 11f; // 점령% 텍스트를 노드 중심 아래로 내리는 간격

    private MinimapModel _model;
    private RawImage     _image;
    private Texture2D    _texture;
    private Color32[]    _buffer;
    private RectTransform _rootRect;
    private RectTransform _frameRoot;
    private bool          _usesHudLayout;

    // z순서 고정 레이어 컨테이너(아래→위). 요소를 부모로 분류해 매 프레임 sibling reorder를 없앤다.
    private RectTransform _roomLayer;      // 방 fill/frame
    private RectTransform _gateRouteLayer; // 게이트 라우트 라인
    private RectTransform _overlayLayer;   // 일반 마커·배지·경로 인디케이터·hub 프레임
    private RectTransform _topLayer;       // 항상 최상단 마커(본진·플레이어)

    private Vector2    _mapMinCell;
    private int        _texWidth;
    private int        _texHeight;
    private float      _cellSize;
    private float      _renderScale;    // 텍스처 px → 이미지 로컬 px
    private float      _worldToImagePx; // 월드 단위 → 이미지 로컬 px (마커/방 공용 스케일)

    private int   _transitionFromIndex = -1; // 이동 중 방향 배지를 그릴 출발 노드
    private int   _transitionToIndex   = -1;
    private int  _currentIndex = -1;
    private bool _wasTransitioning;

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

    // 한 섹터에 고정된 추적 소스(본진 등). 위치=섹터 중심, 게이트 전환 보간 없음. Elite_State가 아니라
    // 마커 숨김 로직(ShouldHideEliteMarker)에 안 걸려 어느 섹터에 있든 항상 그 노드에 표시된다.
    private sealed class SectorTracked : IMinimapTracked
    {
        private readonly Sector _sector;
        private readonly NavFaction _faction;
        public SectorTracked(Sector sector, NavFaction faction) { _sector = sector; _faction = faction; }
        public Sector  Sector        => _sector;
        public Vector3 WorldPosition => _sector != null ? _sector.transform.position : Vector3.zero;
        public Vector3 Forward       => Vector3.forward;
        public NavFaction Faction    => _faction;
        public bool TryGetTransition(out Sector from, out Sector to, out float t)
        {
            from = null; to = null; t = 0f;
            return false;
        }
    }

    private readonly List<Marker> _markers = new();
    private Marker _capitalMarker; // 본진 마커(재바인딩 시 교체).

    private sealed class LinkLine
    {
        public RectTransform Rect;
        public Image Image;
    }

    private readonly List<LinkLine> _gateRoutePool = new(); // 실제 게이트 위치끼리 잇는 라인 풀.
    private readonly List<Image> _hubMarkerPool = new();

    // 배경 섹터 요약 배지(잡몹 병력 숫자 + 점령 게이지%). 풀에서 Text 라벨을 재사용한다.
    private readonly List<Text> _badgePool = new();
    private static Font _badgeFont;

    // 방 오버레이(스프라이트 또는 단색 박스). 상태 변화 시 색만 갱신.
    // Fill=방 내부 이미지(원색 유지), Frame=테두리 이미지(상태/게이지 색을 입힘). Frame이 없으면 Fill 전체를 칠한다.
    private struct RoomVisual
    {
        public int   Index;
        public Image Fill;
        public Image Frame;
    }

    private readonly List<RoomVisual> _rooms = new();
    private readonly List<Image> _outerFrameSegments = new();

    public void Init(MinimapModel model)
    {
        Clear();
        ResolveDefaultSprites();

        _model = model;
        if (_model == null || _model.Nodes.Count == 0) return;

        _cellSize = _model.CellSize;
        BuildCanvas();
        BuildLayers();
        BuildTexture();
        BuildOuterFrame();
        BuildRooms();
        SyncCurrentSector();
        DrawStaticMap(); // 배경 단색 1회 래스터화(통로는 UI 게이트 라우트가 그린다).
        RefreshRoomTints();
    }

    private void ResolveDefaultSprites()
    {
#if UNITY_EDITOR
        playerDirectionSprite ??= UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/01_Assets/UI/Minimap/Arrow2.png");
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
        RefreshRoomTints();  // 점령 게이지 색은 매 프레임 변하므로 항상 갱신.
        RenderGateRoutes();  // 마커보다 먼저: 게이트 라인을 깔고 그 위에 마커/배지가 오도록.
        UpdateMarkers();
    }

    // ── 방 오버레이 (월드 스케일) ────────────────────────────────────────────────
    // 각 방을 nav 바닥 월드 크기에 비례해 그린다. 위치/크기 모두 월드→픽셀 단일 투영이라
    // 실제 footprint에 맞춰 그린 스프라이트가 씬과 1:1로 나오고, 플레이어 마커와도 정렬된다.

    private void BuildRooms()
    {
        foreach (MinimapModel.Node node in _model.Nodes)
        {
            // 내부(fill) 이미지: 방 원본 스프라이트. 색은 항상 원색(흰색) 유지.
            Image fill = CreateRoomImage(node, node.Sprite, "Room");
            // 프레임(테두리) 이미지: fill 위에 얹어 상태/게이지 색을 입힌다. 없으면 fill 전체를 칠한다.
            Image frame = node.FrameSprite != null
                ? CreateRoomImage(node, node.FrameSprite, "RoomFrame")
                : null;
            _rooms.Add(new RoomVisual { Index = node.Index, Fill = fill, Frame = frame });
        }
    }

    // 방 한 장(fill 또는 frame)을 nav 월드 크기·위치·회전에 맞춰 만든다. 두 레이어가 같은 변환을 공유.
    private Image CreateRoomImage(MinimapModel.Node node, Sprite sprite, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(_roomLayer, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f); // 이미지 좌하단 기준
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = RoomSizePx(node);            // 회전 전 크기. 회전은 아래에서 적용
        rt.anchoredPosition = RoomCenterImageLocal(node);
        // 셀 회전((x,y)->(y,-x), 시계)과 같은 방향. 비대칭 스프라이트로 부호 한 번 확인 필요.
        rt.localEulerAngles = new Vector3(0f, 0f, -90f * node.RotationSteps);

        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        if (sprite != null)
        {
            img.sprite         = sprite;
            img.preserveAspect = node.WorldSize.x <= 0f; // 월드 크기 알면 박스에 꽉 채움
        }
        return img;
    }

    private void RefreshRoomTints()
    {
        SectorBattleManager battle = SectorBattleManager.Instance;
        for (int i = 0; i < _rooms.Count; i++)
        {
            RoomVisual room = _rooms[i];
            Color tint = ResolveRoomColor(_model.Nodes[room.Index], battle);
            if (room.Frame != null)
            {
                room.Frame.color = tint;        // 상태 색은 프레임에만.
                room.Fill.color  = Color.white; // 내부는 원색 유지.
            }
            else
            {
                room.Fill.color = tint;         // 프레임 없으면 방 전체에 색(기존 동작).
            }
        }
    }

    // 병력이 있는 섹터는 점령 게이지 색(적 빨강 ~ 아군 파랑)으로 칠한다.
    private Color ResolveRoomColor(MinimapModel.Node node, SectorBattleManager battle)
    {
        if (battle != null && battle.TryGetState(node.Sector, out SectorBattleState state)
            && (state.AllyTotal > 0f || state.EnemyTotal > 0f))
        {
            return Color.Lerp(gaugeEnemyColor, gaugeAllyColor, state.GaugeNormalized);
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

    // 본진(적 코어) 마커를 설정한다. 결전 목표를 항상 표시(어느 섹터에 있든 그 노드에 고정).
    public void SetCapital(Sector capital)
    {
        if (_capitalMarker != null)
        {
            RemoveMarker(_capitalMarker);
            _capitalMarker = null;
        }
        if (capital == null) return;

        _capitalMarker = CreateMarker(
            new SectorTracked(capital, NavFaction.Enemy),
            capitalMarker, followsTransition: false, renderOnTop: true);
    }

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
        rt.SetParent(renderOnTop ? _topLayer : _overlayLayer, false); // 최상단 마커는 TopLayer, 그 외 OverlayLayer.
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

        for (int i = 0; i < _markers.Count; i++)
        {
            Marker marker = _markers[i];
            if (marker.Source == null)
            {
                if (marker.Rect != null) marker.Rect.gameObject.SetActive(false);
                continue;
            }

            // GateArriving(실체화 대쉬 진입): 실체 위치(섹터 밖) 대신 대쉬 진행도로 도착게이트→도착점을 보간한다.
            // 백그라운드 글라이드 끝(도착게이트)과 연속되고 실체 대쉬와 진행도가 동기화된다(확대 무관).
            if (marker.Source is Elite_State arriving && arriving.Presence == ElitePresenceState.GateArriving)
            {
                if (TryGateArrivalGlidePx(arriving, out Vector2 ap, out float arot))
                    ShowMarker(marker, ap, arot, instant: true);
                else
                {
                    marker.Rect.gameObject.SetActive(false);
                    marker.Placed = false;
                }
                continue;
            }

            if (ShouldHideEliteMarker(marker.Source, active)
                && !TryResolveTransition(marker, out _, out _, out _))
            {
                marker.Rect.gameObject.SetActive(false);
                marker.Placed = false;
                continue;
            }

            // 게이트/필드 이동 중(플레이어·엘리트 공용): 섹터 간 게이트 라우트 위를 진행도(t)로 글라이드.
            // 실제 transform 위치는 안 쓰고 게이트 px만 Lerp하므로 2배 확대와 무관하다. 게이트가 없으면 숨긴다
            // (엘리트·플레이어는 게이트로만 섹터를 이동하므로 정상 흐름에선 거의 발생하지 않는다).
            if (TryResolveTransition(marker, out Sector from, out Sector to, out float t))
            {
                if (from != to && TryGateGlidePx(from, to, t, out Vector2 glidePx, out float glideRot))
                {
                    ShowMarker(marker, glidePx, glideRot, instant: true);
                    continue;
                }

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
    }

    // 링크 허브(영향력 큰 점령지)에 capital과 동일한 고정 아이콘 마커를 얹는다. 점령 강조선은 게이트 라우트 색이 대체한다.
    private void RenderStrategicLinks()
    {
        SectorBattleManager battle = SectorBattleManager.Instance;
        int used = 0;

        if (battle != null)
        {
            foreach (KeyValuePair<Sector, SectorBattleState> kv in battle.States)
            {
                SectorBattleState state = kv.Value;
                if (!state.IsLinkHub || state.LinkInfluence < 2) continue;

                int idx = IndexOfSector(state.Sector);
                if (idx < 0) continue;
                SetHubMarker(GetHubMarker(used++), idx);
            }
        }

        for (int i = used; i < _hubMarkerPool.Count; i++)
            _hubMarkerPool[i].gameObject.SetActive(false);
    }

    // ── 게이트 라우트 ───────────────────────────────────────────────────────────
    // 각 엣지마다 양쪽 섹터에서 서로를 향하는 게이트의 실제 월드 위치를 찾아, 방 위 UI 라인으로 잇는다(대각선 포함).
    // 앵커 중심 직선과 달리 "중간을 채우지" 않고 진짜 게이트~게이트 구간만 그리며, 색은 점령 진영으로 칠한다.
    private void RenderGateRoutes()
    {
        int used = 0;
        if (_model != null && _image != null)
        {
            SectorBattleManager battle = SectorBattleManager.Instance;
            for (int i = 0; i < _model.Edges.Count; i++)
            {
                MinimapModel.Edge edge = _model.Edges[i];
                if (edge.A < 0 || edge.B < 0 || edge.A >= _model.Nodes.Count || edge.B >= _model.Nodes.Count)
                    continue;

                MinimapModel.Node na = _model.Nodes[edge.A];
                MinimapModel.Node nb = _model.Nodes[edge.B];
                if (!TryGetGateWorld(na.Sector, nb.Sector, out Vector3 wa)
                    || !TryGetGateWorld(nb.Sector, na.Sector, out Vector3 wb))
                    continue;

                Vector2 pa = WorldToSectorMarkerPx(wa, na);
                Vector2 pb = WorldToSectorMarkerPx(wb, nb);
                Color color = ResolveGateRouteColor(na.Sector, nb.Sector, battle);
                SetGateRouteLine(GetGateRouteLine(used++), pa, pb, color);
            }
        }

        for (int i = used; i < _gateRoutePool.Count; i++)
            _gateRoutePool[i].Rect.gameObject.SetActive(false);
    }

    // 게이트 라우트 색: 양쪽 섹터가 같은 진영 점령이면 그 진영색, 그 외(혼합·미점령)는 중립색.
    private Color ResolveGateRouteColor(Sector a, Sector b, SectorBattleManager battle)
    {
        if (battle != null
            && battle.TryGetState(a, out SectorBattleState sa)
            && battle.TryGetState(b, out SectorBattleState sb)
            && sa.Control == sb.Control)
        {
            if (sa.Control == SectorControl.Ally)  return gateRouteAllyColor;
            if (sa.Control == SectorControl.Enemy) return gateRouteEnemyColor;
        }
        return gateRouteColor;
    }

    // from 섹터의 게이트 중 to 섹터로 연결된 게이트의 월드 위치.
    private static bool TryGetGateWorld(Sector from, Sector to, out Vector3 world)
    {
        world = default;
        if (from == null || to == null) return false;

        SectorGate[] gates = from.Gates;
        if (gates == null) return false;

        for (int i = 0; i < gates.Length; i++)
        {
            SectorGate gate = gates[i];
            if (gate != null && gate.ConnectedGate != null && gate.ConnectedGate.Sector == to)
            {
                world = gate.transform.position;
                return true;
            }
        }
        return false;
    }

    private LinkLine GetGateRouteLine(int index)
    {
        while (_gateRoutePool.Count <= index)
        {
            var go = new GameObject("GateRoute", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_gateRouteLayer, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            _gateRoutePool.Add(new LinkLine { Rect = rt, Image = img });
        }
        return _gateRoutePool[index];
    }

    // 게이트 라우트 라인 갱신. GateRouteLayer 소속이라 방 위에 그려진다(레이어 z순서로 보장, reorder 불필요).
    private void SetGateRouteLine(LinkLine line, Vector2 from, Vector2 to, Color color)
    {
        Vector2 delta = to - from;
        line.Rect.gameObject.SetActive(true);
        line.Rect.anchoredPosition = (from + to) * 0.5f;
        line.Rect.sizeDelta = new Vector2(delta.magnitude, Mathf.Max(1f, gateRouteWidthPx));
        line.Rect.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        line.Image.color = color;
    }

    private Image GetHubMarker(int index)
    {
        while (_hubMarkerPool.Count <= index)
        {
            var go = new GameObject("LinkHubMarker", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_overlayLayer, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            _hubMarkerPool.Add(image);
        }
        return _hubMarkerPool[index];
    }

    // 허브 섹터 중심(앵커)에 고정 크기 아이콘 마커를 얹는다(capital 마커와 동일 방식, sprite 비우면 색 마커).
    private void SetHubMarker(Image marker, int nodeIndex)
    {
        MinimapModel.Node node = _model.Nodes[nodeIndex];
        float px = Mathf.Clamp(
            hubMarker.WorldSize * MarkerWorldToImagePx(),
            hubMarker.MinScreenPx,
            hubMarker.MaxScreenPx);

        RectTransform rt = marker.rectTransform;
        marker.gameObject.SetActive(true);
        marker.sprite = hubMarker.Sprite;
        marker.preserveAspect = hubMarker.Sprite != null;
        marker.color = hubMarker.Color;
        rt.sizeDelta = new Vector2(px, px);
        rt.anchoredPosition = CellToImageLocal(AnchorCenterCell(node));
        rt.localEulerAngles = Vector3.zero;
    }

    private static bool ShouldHideEliteMarker(IMinimapTracked source, Sector active)
    {
        if (source is not Elite_State elite)
            return false;

        return elite.IsInTransit
               || (active != null && elite.CurrentSector == active && elite.Embodiment == null);
    }

    // instant=true면 smoothing 없이 target을 바로 찍는다. 글라이드는 progress로 매 프레임 연속이라
    // smoothing이 lag(뒤처짐)만 만드므로 즉시 반영해야 보이는 속도와 실제 진행이 일치한다.
    private void ShowMarker(Marker marker, Vector2 target, float rotation, bool instant = false)
    {
        marker.Rect.gameObject.SetActive(true);

        if (instant || !marker.Placed)
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
            t    = SectorManager.Instance != null ? SectorManager.Instance.GateTransitionProgress : 0f;
            return true;
        }

        if (marker.Source.TryGetTransition(out from, out to, out t) && to != null)
            return true;

        from = null; to = null; t = 0f;
        return false;
    }

    // 게이트 전환 중 마커가 글라이드할 미니맵 px와 진행 방향(플레이어·엘리트 공용).
    // from→to 섹터를 잇는 양쪽 게이트의 월드 위치를 각 섹터 노드 기준으로 투영해 t로 Lerp한다(실제 위치 미사용).
    private bool TryGateGlidePx(Sector from, Sector to, float t, out Vector2 px, out float rotation)
    {
        px = default;
        rotation = 0f;
        if (from == null || to == null) return false;

        int fi = IndexOfSector(from);
        int ti = IndexOfSector(to);
        if (fi < 0 || ti < 0) return false;

        // from→to, to→from 양쪽에서 서로를 향하는 게이트(=게이트 라우트와 동일한 두 끝점).
        if (!TryGetGateWorld(from, to, out Vector3 wa) || !TryGetGateWorld(to, from, out Vector3 wb))
            return false;

        Vector2 a = WorldToSectorMarkerPx(wa, _model.Nodes[fi]);
        Vector2 b = WorldToSectorMarkerPx(wb, _model.Nodes[ti]);
        px = Vector2.Lerp(a, b, Mathf.Clamp01(t));
        rotation = DirectionToUiRotation(b - a); // 진행 방향(px 공간, up 기준)으로 마커가 향하게.
        return true;
    }

    // GateArriving(실체화 대쉬) 마커 위치: "글라이드가 끊긴 위치(a) → 도착점(b)"을 대쉬 진행도로 보간.
    // a = Lerp(출발 게이트, 도착 게이트, 전환 시점 진행도) → GateApproach 글라이드와 같은 라인·위치라 점프 없이 연속.
    //   (도착 후 실체화면 전환 진행도=1이라 a=도착 게이트, 종전과 동일.)
    // b = 도착점(도착 섹터 안)이라 현재 섹터 마커처럼 확대 일관. 섹터 밖 실제 위치는 안 쓴다.
    private bool TryGateArrivalGlidePx(Elite_State elite, out Vector2 px, out float rotation)
    {
        px = default;
        rotation = 0f;

        Sector to   = elite.CurrentSector;            // 도착 섹터(현재)
        Sector from = elite.GateArrivalOriginSector;  // 출발 섹터
        if (to == null || from == null) return false;

        int ti = IndexOfSector(to);
        int fi = IndexOfSector(from);
        if (ti < 0 || fi < 0) return false;
        if (!TryGetGateWorld(to, from, out Vector3 arrivalGateWorld)) return false; // 도착 섹터의 게이트
        if (!TryGetGateWorld(from, to, out Vector3 departGateWorld)) return false;  // 출발 섹터의 게이트

        MinimapModel.Node toNode = _model.Nodes[ti];
        Vector2 departGatePx  = WorldToSectorMarkerPx(departGateWorld, _model.Nodes[fi]);
        Vector2 arrivalGatePx = WorldToSectorMarkerPx(arrivalGateWorld, toNode);
        // 글라이드가 끊긴 위치(GateApproach 글라이드와 동일 라인의 전환 시점 지점)에서 시작 → 도착 게이트로 점프 방지.
        Vector2 a = Vector2.Lerp(departGatePx, arrivalGatePx, Mathf.Clamp01(elite.GateArrivalStartTravelProgress));
        Vector2 b = WorldToSectorMarkerPx(elite.GateArrivalEndPosition, toNode); // 도착점(섹터 안)
        px = Vector2.Lerp(a, b, Mathf.Clamp01(elite.GateArrivalProgress));
        rotation = DirectionToUiRotation(b - a);
        return true;
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
                if (showSectorControlPercent && (both || ally > 0 || enemy > 0))
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
            rt.SetParent(_overlayLayer, false);
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
        if (previousIndex >= 0 && index >= 0 && HasEdge(previousIndex, index))
        {
            _transitionFromIndex = previousIndex;
            _transitionToIndex   = index;
        }
    }

    private void SyncTransitionState()
    {
        bool isTransitioning = IsGateTransitioning();
        if (isTransitioning == _wasTransitioning) return;

        _wasTransitioning = isTransitioning;
        if (!isTransitioning)
        {
            _transitionFromIndex = -1;
            _transitionToIndex   = -1;
        }
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

    // _image 위에 z순서 고정 레이어를 깐다. 생성 순서가 곧 렌더 순서(아래→위)라 sibling reorder가 불필요해진다.
    private void BuildLayers()
    {
        _roomLayer      = CreateLayer("RoomLayer");
        _gateRouteLayer = CreateLayer("GateRouteLayer");
        _overlayLayer   = CreateLayer("OverlayLayer");
        _topLayer       = CreateLayer("TopLayer");
    }

    // _image 영역을 그대로 덮는 빈 컨테이너(stretch+offset0). 자식 좌표계가 _image와 동일해 좌표 로직 변경 불필요.
    private RectTransform CreateLayer(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(_image.rectTransform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        return rt;
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

    private void BuildOuterFrame()
    {
        if (!showOuterFrame || _image == null) return;

        RectTransform imageRect = _image.rectTransform;
        Vector2 imageSize = imageRect.sizeDelta;
        float padding = Mathf.Max(0f, outerFramePaddingPx);
        Vector2 frameSize = imageSize + Vector2.one * padding * 2f;
        Vector2 pivot = imageRect.pivot;

        var go = new GameObject("MapFrame", typeof(RectTransform));
        _frameRoot = go.GetComponent<RectTransform>();
        _frameRoot.SetParent(_rootRect != null ? _rootRect : transform, false);
        _frameRoot.anchorMin = _frameRoot.anchorMax = _frameRoot.pivot = pivot;
        _frameRoot.anchoredPosition = imageRect.anchoredPosition + new Vector2(
            (pivot.x * 2f - 1f) * padding,
            (pivot.y * 2f - 1f) * padding);
        _frameRoot.sizeDelta = frameSize;

        // 프레임 스프라이트가 있으면 9-slice 한 장으로 영역에 맞춰 늘린다(코너 보존). 없으면 코너 선분 폴백.
        if (outerFrameSprite != null)
        {
            BuildOuterFrameSprite();
            _frameRoot.SetAsLastSibling();
            return;
        }

        float thickness = Mathf.Max(1f, outerFrameThicknessPx);
        float cornerLength = Mathf.Clamp(
            outerFrameCornerLengthPx,
            thickness,
            Mathf.Min(frameSize.x, frameSize.y) * 0.45f);
        float halfW = frameSize.x * 0.5f;
        float halfH = frameSize.y * 0.5f;
        float halfThickness = thickness * 0.5f;
        float halfLength = cornerLength * 0.5f;

        AddFrameSegment("TopLeftHorizontal", new Vector2(-halfW + halfLength, halfH - halfThickness), new Vector2(cornerLength, thickness));
        AddFrameSegment("TopLeftVertical", new Vector2(-halfW + halfThickness, halfH - halfLength), new Vector2(thickness, cornerLength));
        AddFrameSegment("TopRightHorizontal", new Vector2(halfW - halfLength, halfH - halfThickness), new Vector2(cornerLength, thickness));
        AddFrameSegment("TopRightVertical", new Vector2(halfW - halfThickness, halfH - halfLength), new Vector2(thickness, cornerLength));
        AddFrameSegment("BottomLeftHorizontal", new Vector2(-halfW + halfLength, -halfH + halfThickness), new Vector2(cornerLength, thickness));
        AddFrameSegment("BottomLeftVertical", new Vector2(-halfW + halfThickness, -halfH + halfLength), new Vector2(thickness, cornerLength));
        AddFrameSegment("BottomRightHorizontal", new Vector2(halfW - halfLength, -halfH + halfThickness), new Vector2(cornerLength, thickness));
        AddFrameSegment("BottomRightVertical", new Vector2(halfW - halfThickness, -halfH + halfLength), new Vector2(thickness, cornerLength));

        _frameRoot.SetAsLastSibling();
    }

    private void AddFrameSegment(string name, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(_frameRoot, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;

        var image = go.AddComponent<Image>();
        image.color = outerFrameColor;
        image.raycastTarget = false;
        _outerFrameSegments.Add(image);
    }

    // 9-slice 프레임 한 장을 _frameRoot 전체에 깐다. 코너는 border 크기 그대로 유지되고 변만 늘어난다.
    private void BuildOuterFrameSprite()
    {
        var go = new GameObject("FrameImage", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(_frameRoot, false);
        rt.anchorMin = Vector2.zero;   // _frameRoot 영역을 꽉 채움(영역에 맞춰 늘어남).
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.sprite        = outerFrameSprite;
        image.type          = Image.Type.Sliced; // 9-slice: 코너 보존, 변만 늘어남.
        image.fillCenter    = outerFrameFillCenter; // 보통 false라야 가운데로 맵이 비친다.
        image.color         = outerFrameColor;
        image.raycastTarget = false;
        _outerFrameSegments.Add(image);
    }

    // 배경 단색 텍스처. 통로는 UI 게이트 라우트가 그리므로 여기선 배경판만 칠한다.
    private void DrawStaticMap()
    {
        FillAll(backgroundColor);
        _texture.SetPixels32(_buffer);
        _texture.Apply(false);
    }

    private Color ResolveColor(MinimapModel.Node node)
    {
        if (IsGateTransitioning() && node.Index == _currentIndex) return TransitionColor;
        if (node.Index == _currentIndex)   return FallbackCurrentColor;
        if (node.IsStart)                  return FallbackStartColor;
        return FallbackRoomColor;
    }

    private static bool IsGateTransitioning()
        => SectorManager.Instance != null && SectorManager.Instance.IsTransitioning;

    private static Vector2 AnchorCenterCell(MinimapModel.Node node)
        => new Vector2(node.AnchorCell.x + 0.5f, node.AnchorCell.y + 0.5f);

    private void FillAll(Color color)
    {
        Color32 c = color;
        for (int i = 0; i < _buffer.Length; i++) _buffer[i] = c;
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
        _frameRoot = null;
        _roomLayer = _gateRouteLayer = _overlayLayer = _topLayer = null;
        _cellSize = 0f;
        _renderScale = 0f;
        _worldToImagePx = 0f;
        _transitionFromIndex = -1;
        _transitionToIndex = -1;
        _rooms.Clear();
        _markers.Clear();
        _badgePool.Clear();   // 배지 GameObject는 MapImage 자식이라 아래에서 함께 파괴된다.
        _gateRoutePool.Clear();
        _hubMarkerPool.Clear();
        _outerFrameSegments.Clear();
        _currentIndex = -1;
        _wasTransitioning = false;

        if (_texture != null)
        {
            Destroy(_texture);
            _texture = null;
        }

        Transform t = contentRoot != null ? contentRoot : transform;
        for (int i = t.childCount - 1; i >= 0; i--)
        {
            Transform child = t.GetChild(i);
            if (child.name == "MapImage" || child.name == "MapFrame")
                Destroy(child.gameObject);
        }
    }
}
