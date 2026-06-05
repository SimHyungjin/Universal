using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(MapNavigationAuthoring))]
public sealed class MapNavigationAuthoringEditor : Editor
{
    private enum EditSpace
    {
        Region,
        Transition,
        Obstacle
    }

    private EditSpace _editSpace;
    private int _selectedSpaceIndex;
    private int _selectedShapeIndex;
    private int _selectedObstacleIndex;
    private int _selectedPointIndex;
    private int _selectedEdgeIndex = -1;
    private int _circleSegments = 16;
    private float _circleRadius = 3f;
    private bool _snapEnabled = true;
    private bool _filterSceneToSelectedLayer;

    private static readonly Color RegionColor = new(1f, 0.55f, 0.2f);
    private static readonly Color TransitionColor = new(0.35f, 0.65f, 1f);
    private static readonly Color ObstacleColor = new(0.8f, 0.25f, 1f);
    private static readonly Color SelectedRegionColor = new(1f, 0.92f, 0.12f);
    private static readonly Color SelectedTransitionColor = new(0.15f, 1f, 1f);
    private static readonly Color SelectedObstacleColor = new(1f, 0.25f, 0.75f);
    private static readonly Color VertexHandleColor = new(0.15f, 0.75f, 1f);
    private static readonly Color SelectedVertexHandleColor = Color.white;
    private static readonly Color EdgeHandleColor = new(0.1f, 1f, 0.35f);
    private static readonly Color SelectedEdgeHandleColor = new(1f, 0.95f, 0.1f);
    private static readonly Color ShapeSelectColor = new(0.1f, 1f, 0.35f);

    public override void OnInspectorGUI()
    {
        MapNavigationAuthoring map = (MapNavigationAuthoring)target;

        serializedObject.Update();

        DrawSceneEditingInspector(map);
        EditorGUILayout.Space(6f);
        DrawCreateInspector(map);
        EditorGUILayout.Space(6f);
        DrawObstacleBakeInspector(map);
    }

    private void DrawSceneEditingInspector(MapNavigationAuthoring map)
    {
        EditorGUILayout.LabelField("Scene Editing", EditorStyles.boldLabel);

        if (map.Regions.Count > 0)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _filterSceneToSelectedLayer = EditorGUILayout.ToggleLeft(
                        "Filter height", _filterSceneToSelectedLayer, GUILayout.Width(110f));
                    _snapEnabled = EditorGUILayout.ToggleLeft(
                        "Snap drag", _snapEnabled, GUILayout.Width(100f));
                    GUILayout.FlexibleSpace();
                }

                EditSpace nextEditSpace = (EditSpace)EditorGUILayout.EnumPopup("Edit Space", _editSpace);
                if (nextEditSpace != _editSpace)
                {
                    _editSpace = nextEditSpace;
                    _selectedSpaceIndex = 0;
                    _selectedObstacleIndex = 0;
                    _selectedPointIndex = 0;
                    _selectedEdgeIndex = -1;
                }

                int maxSpaceIndex = GetMaxSpaceIndex(map);
                using (new EditorGUI.DisabledScope(maxSpaceIndex < 0))
                {
                    int nextSpaceIndex = EditorGUILayout.IntSlider("Space", Mathf.Clamp(_selectedSpaceIndex, 0, Mathf.Max(0, maxSpaceIndex)), 0, Mathf.Max(0, maxSpaceIndex));
                    if (nextSpaceIndex != _selectedSpaceIndex)
                        SelectSpace(nextSpaceIndex);

                    if (_editSpace == EditSpace.Region)
                    {
                        int maxShapeIndex = GetMaxShapeIndex(map);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            using (new EditorGUI.DisabledScope(maxShapeIndex < 0))
                            {
                                int nextShapeIndex = EditorGUILayout.IntSlider("Shape", Mathf.Clamp(_selectedShapeIndex, 0, Mathf.Max(0, maxShapeIndex)), 0, Mathf.Max(0, maxShapeIndex));
                                if (nextShapeIndex != _selectedShapeIndex)
                                {
                                    _selectedShapeIndex = nextShapeIndex;
                                    _selectedPointIndex = 0;
                                    _selectedEdgeIndex = -1;
                                }
                            }
                            if (GUILayout.Button("+", GUILayout.Width(28f)))
                                AddShape(map);
                        }
                    }

                    if (_editSpace == EditSpace.Obstacle)
                    {
                        int maxObstacleIndex = GetMaxObstacleIndex(map);
                        using (new EditorGUI.DisabledScope(maxObstacleIndex < 0))
                        {
                            int nextObstacleIndex = EditorGUILayout.IntSlider("Obstacle", Mathf.Clamp(_selectedObstacleIndex, 0, Mathf.Max(0, maxObstacleIndex)), 0, Mathf.Max(0, maxObstacleIndex));
                            if (nextObstacleIndex != _selectedObstacleIndex)
                                SelectObstacle(nextObstacleIndex);
                        }
                    }

                    int maxPointIndex = GetMaxPointIndex(map);
                    int nextPointIndex = EditorGUILayout.IntSlider("Point", Mathf.Clamp(_selectedPointIndex, 0, Mathf.Max(0, maxPointIndex)), 0, Mathf.Max(0, maxPointIndex));
                    if (nextPointIndex != _selectedPointIndex)
                        _selectedPointIndex = nextPointIndex;

                    int maxEdgeIndex = GetMaxEdgeIndex(map);
                    using (new EditorGUI.DisabledScope(maxEdgeIndex < 0))
                    {
                        int shownEdgeIndex = Mathf.Clamp(_selectedEdgeIndex, 0, Mathf.Max(0, maxEdgeIndex));
                        int nextEdgeIndex = EditorGUILayout.IntSlider("Edge", shownEdgeIndex, 0, Mathf.Max(0, maxEdgeIndex));
                        if (nextEdgeIndex != _selectedEdgeIndex)
                            _selectedEdgeIndex = nextEdgeIndex;
                    }

                    if (_editSpace == EditSpace.Transition)
                        DrawSelectedTransitionHeightFields(map);
                }
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Add a region to enable scene editing.", MessageType.Info);
        }

        EditorGUI.BeginChangeCheck();
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("regions"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("transitions"), true);
        }
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            map.RebuildRuntimeData();
            SceneView.RepaintAll();
        }
    }

    private void DrawCreateInspector(MapNavigationAuthoring map)
    {
        EditorGUILayout.LabelField("Create", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Flat Region"))
                    AddFlatRegion(map);

                using (new EditorGUI.DisabledScope(_selectedSpaceIndex < 0 || _selectedSpaceIndex >= map.Regions.Count))
                {
                    if (GUILayout.Button("Shape"))
                        AddShape(map);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Stair Transition"))
                    AddTransition(map, MapNavTransitionType.Stair);

                using (new EditorGUI.DisabledScope(_editSpace != EditSpace.Region || _selectedSpaceIndex < 0 || _selectedSpaceIndex >= map.Regions.Count))
                {
                    if (GUILayout.Button("Connect Edge"))
                        AddAutoEdgeTransition(map);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Obstacle"))
                    AddObstacle(map);
            }

            _circleSegments = EditorGUILayout.IntSlider("Circle Segments", _circleSegments, 3, 64);
            _circleRadius = Mathf.Max(0.1f, EditorGUILayout.FloatField("Circle Radius", _circleRadius));
            if (GUILayout.Button("Add Circle Region"))
                AddCircleRegion(map, _circleSegments, _circleRadius);

            if (GUILayout.Button("Validate Navigation"))
                LogValidation(map);
        }
    }

    private void DrawObstacleBakeInspector(MapNavigationAuthoring map)
    {
        EditorGUILayout.LabelField("Obstacle Bake", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUI.BeginChangeCheck();
            LayerMask newMask = EditorGUILayout.MaskField(
                "Obstacle Layer Mask",
                InternalEditorUtility.LayerMaskToConcatenatedLayersMask(map.ObstacleLayerMask),
                InternalEditorUtility.layers);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(map, "Set Obstacle Layer Mask");
                map.ObstacleLayerMask = InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(newMask);
                EditorUtility.SetDirty(map);
            }

            EditorGUI.BeginChangeCheck();
            LayerMask newWallMask = EditorGUILayout.MaskField(
                "Wall Obstacle Layer Mask",
                InternalEditorUtility.LayerMaskToConcatenatedLayersMask(map.WallObstacleLayerMask),
                InternalEditorUtility.layers);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(map, "Set Wall Obstacle Layer Mask");
                map.WallObstacleLayerMask = InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(newWallMask);
                EditorUtility.SetDirty(map);
            }

            EditorGUI.BeginChangeCheck();
            float newTol = EditorGUILayout.FloatField("Height Tolerance", map.ObstacleHeightTolerance);
            float newPad = EditorGUILayout.FloatField("Corner Padding", map.DefaultObstacleCornerPadding);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(map, "Set Obstacle Bake Settings");
                map.ObstacleHeightTolerance = Mathf.Max(0f, newTol);
                map.DefaultObstacleCornerPadding = Mathf.Max(0f, newPad);
                EditorUtility.SetDirty(map);
            }

            using (new EditorGUI.DisabledScope(map.ObstacleLayerMask == 0 && map.WallObstacleLayerMask == 0))
            {
                if (GUILayout.Button("Bake Obstacles from Colliders"))
                {
                    if (EditorUtility.DisplayDialog(
                        "Bake Nav Obstacles",
                        "기존 Obstacle이 모두 지워지고 콜라이더에서 재추출됩니다.\n계속하시겠습니까?",
                        "Bake", "Cancel"))
                    {
                        MapNavObstacleExtractor.Extract(map);
                        SceneView.RepaintAll();
                    }
                }
            }

            if (map.ObstacleLayerMask == 0 && map.WallObstacleLayerMask == 0)
                EditorGUILayout.HelpBox("Obstacle/Wall Layer Mask를 먼저 설정하세요.", MessageType.Info);
        }
    }

    private void OnSceneGUI()
    {
        MapNavigationAuthoring map = (MapNavigationAuthoring)target;
        DrawRegions(map);
        DrawTransitions(map);
        DrawObstacles(map);

        if (_editSpace == EditSpace.Region)
            EditSelectedRegion(map);
        else if (_editSpace == EditSpace.Transition)
            EditSelectedTransition(map);
        else
            EditSelectedObstacle(map);

        HandleSceneInput(map);
    }

    private void HandleSceneInput(MapNavigationAuthoring map)
    {
        Event e = Event.current;
        if (e == null)
            return;

        // Keep Scene input passive here. Handles own drag/selection so authoring data is not changed accidentally.
    }

    private bool TrySelectAtMouse(MapNavigationAuthoring map, Vector2 mousePosition)
    {
        if (TrySelectPointAtMouse(map, mousePosition, 12f))
            return true;

        if (TrySelectEdgeAtMouse(map, mousePosition, 10f))
            return true;

        return TrySelectShapeAtMouse(map, mousePosition);
    }

    private bool TrySelectPointAtMouse(MapNavigationAuthoring map, Vector2 mousePosition, float thresholdPixels)
    {
        float bestSqr = thresholdPixels * thresholdPixels;
        bool found = false;
        EditSpace bestSpace = _editSpace;
        int bestSpaceIndex = _selectedSpaceIndex;
        int bestShapeIndex = _selectedShapeIndex;
        int bestObstacleIndex = _selectedObstacleIndex;
        int bestPointIndex = _selectedPointIndex;

        for (int r = 0; r < map.Regions.Count; r++)
        {
            MapNavRegion region = map.Regions[r];
            if (region?.Shapes != null && ShouldDrawRegion(region))
            {
                for (int s = 0; s < region.Shapes.Count; s++)
                    CheckPointHit(map, region, region.Shapes[s]?.Points, EditSpace.Region, r, s, 0, mousePosition, ref bestSqr, ref found, ref bestSpace, ref bestSpaceIndex, ref bestShapeIndex, ref bestObstacleIndex, ref bestPointIndex);
            }

            if (region?.Obstacles == null || !ShouldDrawRegion(region))
                continue;

            for (int o = 0; o < region.Obstacles.Count; o++)
                CheckPointHit(map, region, region.Obstacles[o]?.Points, EditSpace.Obstacle, r, 0, o, mousePosition, ref bestSqr, ref found, ref bestSpace, ref bestSpaceIndex, ref bestShapeIndex, ref bestObstacleIndex, ref bestPointIndex);
        }

        for (int t = 0; t < map.Transitions.Count; t++)
        {
            MapNavTransition transition = map.Transitions[t];
            if (transition == null || !ShouldDrawTransition(map, transition))
                continue;

            CheckPointHit(map, transition, transition.Points, EditSpace.Transition, t, 0, 0, mousePosition, ref bestSqr, ref found, ref bestSpace, ref bestSpaceIndex, ref bestShapeIndex, ref bestObstacleIndex, ref bestPointIndex);
        }

        if (!found)
            return false;

        SetSelection(bestSpace, bestSpaceIndex, bestShapeIndex, bestObstacleIndex, bestPointIndex, -1);
        return true;
    }

    private bool TrySelectEdgeAtMouse(MapNavigationAuthoring map, Vector2 mousePosition, float thresholdPixels)
    {
        float bestDistance = thresholdPixels;
        bool found = false;
        EditSpace bestSpace = _editSpace;
        int bestSpaceIndex = _selectedSpaceIndex;
        int bestShapeIndex = _selectedShapeIndex;
        int bestObstacleIndex = _selectedObstacleIndex;
        int bestEdgeIndex = _selectedEdgeIndex;

        for (int r = 0; r < map.Regions.Count; r++)
        {
            MapNavRegion region = map.Regions[r];
            if (region?.Shapes != null && ShouldDrawRegion(region))
            {
                for (int s = 0; s < region.Shapes.Count; s++)
                    CheckEdgeHit(map, region, region.Shapes[s]?.Points, EditSpace.Region, r, s, 0, mousePosition, ref bestDistance, ref found, ref bestSpace, ref bestSpaceIndex, ref bestShapeIndex, ref bestObstacleIndex, ref bestEdgeIndex);
            }

            if (region?.Obstacles == null || !ShouldDrawRegion(region))
                continue;

            for (int o = 0; o < region.Obstacles.Count; o++)
                CheckEdgeHit(map, region, region.Obstacles[o]?.Points, EditSpace.Obstacle, r, 0, o, mousePosition, ref bestDistance, ref found, ref bestSpace, ref bestSpaceIndex, ref bestShapeIndex, ref bestObstacleIndex, ref bestEdgeIndex);
        }

        for (int t = 0; t < map.Transitions.Count; t++)
        {
            MapNavTransition transition = map.Transitions[t];
            if (transition == null || !ShouldDrawTransition(map, transition))
                continue;

            CheckEdgeHit(map, transition, transition.Points, EditSpace.Transition, t, 0, 0, mousePosition, ref bestDistance, ref found, ref bestSpace, ref bestSpaceIndex, ref bestShapeIndex, ref bestObstacleIndex, ref bestEdgeIndex);
        }

        if (!found)
            return false;

        SetSelection(bestSpace, bestSpaceIndex, bestShapeIndex, bestObstacleIndex, bestEdgeIndex, bestEdgeIndex);
        return true;
    }

    private bool TrySelectShapeAtMouse(MapNavigationAuthoring map, Vector2 mousePosition)
    {
        for (int r = 0; r < map.Regions.Count; r++)
        {
            MapNavRegion region = map.Regions[r];
            if (region == null || !ShouldDrawRegion(region))
                continue;

            if (TryMouseToLocal(map, region.Height, mousePosition, out Vector2 local))
            {
                if (region.Obstacles != null)
                {
                    for (int o = 0; o < region.Obstacles.Count; o++)
                    {
                        MapNavObstacle obstacle = region.Obstacles[o];
                        if (obstacle?.Points != null && MapNavGeometry.ContainsPoint(obstacle.Points, local))
                        {
                            SetSelection(EditSpace.Obstacle, r, 0, o, 0, -1);
                            return true;
                        }
                    }
                }

                if (region.Shapes != null)
                {
                    for (int s = 0; s < region.Shapes.Count; s++)
                    {
                        MapNavPolygon shape = region.Shapes[s];
                        if (shape?.Points != null && MapNavGeometry.ContainsPoint(shape.Points, local))
                        {
                            SetSelection(EditSpace.Region, r, s, 0, 0, -1);
                            return true;
                        }
                    }
                }
            }
        }

        for (int t = 0; t < map.Transitions.Count; t++)
        {
            MapNavTransition transition = map.Transitions[t];
            if (transition == null || !ShouldDrawTransition(map, transition))
                continue;

            float height = Mathf.Lerp(transition.FromHeight, transition.ToHeight, 0.5f);
            if (TryMouseToLocal(map, height, mousePosition, out Vector2 local)
                && MapNavGeometry.ContainsPoint(transition.Points, local))
            {
                SetSelection(EditSpace.Transition, t, 0, 0, 0, -1);
                return true;
            }
        }

        return false;
    }

    private static void CheckPointHit(
        MapNavigationAuthoring map,
        MapNavRegion region,
        List<Vector2> points,
        EditSpace space,
        int spaceIndex,
        int shapeIndex,
        int obstacleIndex,
        Vector2 mousePosition,
        ref float bestSqr,
        ref bool found,
        ref EditSpace bestSpace,
        ref int bestSpaceIndex,
        ref int bestShapeIndex,
        ref int bestObstacleIndex,
        ref int bestPointIndex)
    {
        if (points == null)
            return;

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 gui = HandleUtility.WorldToGUIPoint(map.ToWorld(region, points[i]));
            float sqr = (gui - mousePosition).sqrMagnitude;
            if (sqr >= bestSqr)
                continue;

            bestSqr = sqr;
            found = true;
            bestSpace = space;
            bestSpaceIndex = spaceIndex;
            bestShapeIndex = shapeIndex;
            bestObstacleIndex = obstacleIndex;
            bestPointIndex = i;
        }
    }

    private static void CheckPointHit(
        MapNavigationAuthoring map,
        MapNavTransition transition,
        List<Vector2> points,
        EditSpace space,
        int spaceIndex,
        int shapeIndex,
        int obstacleIndex,
        Vector2 mousePosition,
        ref float bestSqr,
        ref bool found,
        ref EditSpace bestSpace,
        ref int bestSpaceIndex,
        ref int bestShapeIndex,
        ref int bestObstacleIndex,
        ref int bestPointIndex)
    {
        if (points == null)
            return;

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 gui = HandleUtility.WorldToGUIPoint(map.ToWorld(transition, points[i]));
            float sqr = (gui - mousePosition).sqrMagnitude;
            if (sqr >= bestSqr)
                continue;

            bestSqr = sqr;
            found = true;
            bestSpace = space;
            bestSpaceIndex = spaceIndex;
            bestShapeIndex = shapeIndex;
            bestObstacleIndex = obstacleIndex;
            bestPointIndex = i;
        }
    }

    private static void CheckEdgeHit(
        MapNavigationAuthoring map,
        MapNavRegion region,
        List<Vector2> points,
        EditSpace space,
        int spaceIndex,
        int shapeIndex,
        int obstacleIndex,
        Vector2 mousePosition,
        ref float bestDistance,
        ref bool found,
        ref EditSpace bestSpace,
        ref int bestSpaceIndex,
        ref int bestShapeIndex,
        ref int bestObstacleIndex,
        ref int bestEdgeIndex)
    {
        if (points == null || points.Count < 2)
            return;

        for (int i = 0, previousIndex = points.Count - 1; i < points.Count; previousIndex = i++)
        {
            Vector2 a = HandleUtility.WorldToGUIPoint(map.ToWorld(region, points[previousIndex]));
            Vector2 b = HandleUtility.WorldToGUIPoint(map.ToWorld(region, points[i]));
            float distance = DistancePointToSegment(mousePosition, a, b);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            found = true;
            bestSpace = space;
            bestSpaceIndex = spaceIndex;
            bestShapeIndex = shapeIndex;
            bestObstacleIndex = obstacleIndex;
            bestEdgeIndex = i;
        }
    }

    private static void CheckEdgeHit(
        MapNavigationAuthoring map,
        MapNavTransition transition,
        List<Vector2> points,
        EditSpace space,
        int spaceIndex,
        int shapeIndex,
        int obstacleIndex,
        Vector2 mousePosition,
        ref float bestDistance,
        ref bool found,
        ref EditSpace bestSpace,
        ref int bestSpaceIndex,
        ref int bestShapeIndex,
        ref int bestObstacleIndex,
        ref int bestEdgeIndex)
    {
        if (points == null || points.Count < 2)
            return;

        for (int i = 0, previousIndex = points.Count - 1; i < points.Count; previousIndex = i++)
        {
            Vector2 a = HandleUtility.WorldToGUIPoint(map.ToWorld(transition, points[previousIndex]));
            Vector2 b = HandleUtility.WorldToGUIPoint(map.ToWorld(transition, points[i]));
            float distance = DistancePointToSegment(mousePosition, a, b);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            found = true;
            bestSpace = space;
            bestSpaceIndex = spaceIndex;
            bestShapeIndex = shapeIndex;
            bestObstacleIndex = obstacleIndex;
            bestEdgeIndex = i;
        }
    }

    private void SetSelection(EditSpace space, int spaceIndex, int shapeIndex, int obstacleIndex, int pointIndex, int edgeIndex)
    {
        _editSpace = space;
        _selectedSpaceIndex = spaceIndex;
        _selectedShapeIndex = shapeIndex;
        _selectedObstacleIndex = obstacleIndex;
        _selectedPointIndex = pointIndex;
        _selectedEdgeIndex = edgeIndex;
        SceneView.RepaintAll();
    }

    private bool AddPointAtMouse(MapNavigationAuthoring map, Vector2 mousePosition)
    {
        if (TrySelectEdgeAtMouse(map, mousePosition, 14f))
            return InsertPointOnSelectedEdge(map, mousePosition);

        return InsertPointOnSelectedEdge(map, mousePosition);
    }

    private bool InsertPointOnSelectedEdge(MapNavigationAuthoring map, Vector2 mousePosition)
    {
        if (!TryGetSelectedPoints(map, out List<Vector2> points, out float height, out string undoName))
            return false;

        if (points.Count < 2)
            return false;

        int edgeIndex = _selectedEdgeIndex >= 0 ? _selectedEdgeIndex : FindNearestEdgeIndex(map, points, height, mousePosition);
        if (edgeIndex < 0)
            return false;

        if (!TryMouseToLocal(map, height, mousePosition, out Vector2 local))
            return false;

        Undo.RecordObject(map, undoName);
        points.Insert(edgeIndex, local);
        _selectedPointIndex = edgeIndex;
        _selectedEdgeIndex = -1;
        map.RebuildRuntimeData();
        EditorUtility.SetDirty(map);
        SceneView.RepaintAll();
        return true;
    }

    private bool DeleteSelectedPointOrEdge(MapNavigationAuthoring map)
    {
        if (!TryGetSelectedPoints(map, out List<Vector2> points, out _, out string undoName))
            return false;

        int removeIndex = _selectedPointIndex;
        if (_selectedEdgeIndex >= 0)
            removeIndex = _selectedEdgeIndex;

        if (removeIndex < 0 || removeIndex >= points.Count)
            return false;

        int minPointCount = _editSpace == EditSpace.Transition ? 2 : 3;
        if (points.Count <= minPointCount)
            return false;

        Undo.RecordObject(map, undoName);
        points.RemoveAt(removeIndex);
        _selectedPointIndex = Mathf.Clamp(removeIndex, 0, points.Count - 1);
        _selectedEdgeIndex = -1;
        map.RebuildRuntimeData();
        EditorUtility.SetDirty(map);
        SceneView.RepaintAll();
        return true;
    }

    private bool TryGetSelectedPoints(MapNavigationAuthoring map, out List<Vector2> points, out float height, out string undoName)
    {
        points = null;
        height = 0f;
        undoName = "Edit Map Navigation Points";

        if (_editSpace == EditSpace.Region)
        {
            if (_selectedSpaceIndex < 0 || _selectedSpaceIndex >= map.Regions.Count)
                return false;

            MapNavRegion region = map.Regions[_selectedSpaceIndex];
            MapNavPolygon shape = GetSelectedShape(region);
            if (region == null || shape?.Points == null)
                return false;

            points = shape.Points;
            height = region.Height;
            undoName = "Edit Map Navigation Region Points";
            return true;
        }

        if (_editSpace == EditSpace.Transition)
        {
            if (_selectedSpaceIndex < 0 || _selectedSpaceIndex >= map.Transitions.Count)
                return false;

            MapNavTransition transition = map.Transitions[_selectedSpaceIndex];
            if (transition?.Points == null)
                return false;

            points = transition.Points;
            height = Mathf.Lerp(transition.FromHeight, transition.ToHeight, 0.5f);
            undoName = "Edit Map Navigation Transition Points";
            return true;
        }

        if (!TryGetSelectedObstacle(map, out MapNavRegion obstacleRegion, out MapNavObstacle obstacle))
            return false;

        points = obstacle.Points;
        height = obstacleRegion.Height;
        undoName = "Edit Map Navigation Obstacle Points";
        return points != null;
    }

    private int FindNearestEdgeIndex(MapNavigationAuthoring map, List<Vector2> points, float height, Vector2 mousePosition)
    {
        if (points == null || points.Count < 2)
            return -1;

        float bestDistance = 14f;
        int bestIndex = -1;
        for (int i = 0, previousIndex = points.Count - 1; i < points.Count; previousIndex = i++)
        {
            Vector3 worldA = map.transform.TransformPoint(new Vector3(points[previousIndex].x, height, points[previousIndex].y));
            Vector3 worldB = map.transform.TransformPoint(new Vector3(points[i].x, height, points[i].y));
            float distance = DistancePointToSegment(mousePosition, HandleUtility.WorldToGUIPoint(worldA), HandleUtility.WorldToGUIPoint(worldB));
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }

        return bestIndex;
    }

    private static bool TryMouseToLocal(MapNavigationAuthoring map, float localHeight, Vector2 mousePosition, out Vector2 local)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        Vector3 planePoint = map.transform.TransformPoint(new Vector3(0f, localHeight, 0f));
        Plane plane = new(map.transform.up, planePoint);
        if (!plane.Raycast(ray, out float enter))
        {
            local = default;
            return false;
        }

        Vector3 local3 = map.transform.InverseTransformPoint(ray.GetPoint(enter));
        local = new Vector2(local3.x, local3.z);
        return true;
    }

    private static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float sqrMagnitude = ab.sqrMagnitude;
        if (sqrMagnitude <= 1e-6f)
            return Vector2.Distance(point, a);

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / sqrMagnitude);
        return Vector2.Distance(point, a + ab * t);
    }

    private bool ShouldDrawRegion(MapNavRegion region)
    {
        if (!_filterSceneToSelectedLayer)
            return true;

        return Mathf.Abs(region.Height - GetSelectedRegionHeight()) <= 0.001f;
    }

    // Scene Editing에서 현재 선택된 리전의 높이. Filter To Selected Layer의 기준이 된다.
    private float GetSelectedRegionHeight()
    {
        var map = (MapNavigationAuthoring)target;
        if (_selectedSpaceIndex >= 0 && _selectedSpaceIndex < map.Regions.Count)
        {
            MapNavRegion r = map.Regions[_selectedSpaceIndex];
            if (r != null) return r.Height;
        }
        return 0f;
    }

    // 드래그한 점을 다른 리전 꼭짓점(xz 평면)에 자석처럼 붙인다. 리전 연결(portal)은 꼭짓점이 정확히
    // 맞닿아야 성립하므로 수동 정렬을 보조한다. excludeShape+excludeIndex는 드래그 중인 자기 점을 제외한다.
    private Vector3 SnapWorldPoint(MapNavigationAuthoring map, Vector3 worldMoved, MapNavPolygon excludeShape, int excludeIndex)
    {
        if (!_snapEnabled)
            return worldMoved;

        float threshold = HandleUtility.GetHandleSize(worldMoved) * 0.18f;
        float bestSqr = threshold * threshold;
        Vector3 best = worldMoved;
        bool found = false;

        IReadOnlyList<MapNavRegion> regions = map.Regions;
        for (int r = 0; r < regions.Count; r++)
        {
            MapNavRegion region = regions[r];
            if (region?.Shapes == null) continue;
            for (int s = 0; s < region.Shapes.Count; s++)
            {
                MapNavPolygon shape = region.Shapes[s];
                if (shape?.Points == null) continue;
                for (int i = 0; i < shape.Points.Count; i++)
                {
                    if (ReferenceEquals(shape, excludeShape) && i == excludeIndex) continue;
                    Vector3 w = map.ToWorld(region, shape.Points[i]);
                    float dx = w.x - worldMoved.x;
                    float dz = w.z - worldMoved.z;
                    float sqr = dx * dx + dz * dz;
                    if (sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        best = new Vector3(w.x, worldMoved.y, w.z);
                        found = true;
                    }
                }
            }
        }
        return found ? best : worldMoved;
    }

    private bool ShouldDrawTransition(MapNavigationAuthoring map, MapNavTransition transition)
    {
        if (!_filterSceneToSelectedLayer)
            return true;

        IReadOnlyList<MapNavRegion> regions = map.Regions;
        MapNavRegion fromRegion = FindRegion(regions, transition.FromRegionId);
        MapNavRegion toRegion = FindRegion(regions, transition.ToRegionId);
        return (fromRegion != null && ShouldDrawRegion(fromRegion))
            || (toRegion != null && ShouldDrawRegion(toRegion));
    }

    private void DrawRegions(MapNavigationAuthoring map)
    {
        for (int i = 0; i < map.Regions.Count; i++)
        {
            MapNavRegion region = map.Regions[i];
            if (region?.Shapes == null || region.Shapes.Count == 0)
                continue;
            if (!ShouldDrawRegion(region))
                continue;

            bool isSelected = _editSpace == EditSpace.Region && i == _selectedSpaceIndex;
            Color baseColor = isSelected ? SelectedRegionColor : RegionColor;

            for (int si = 0; si < region.Shapes.Count; si++)
            {
                MapNavPolygon shape = region.Shapes[si];
                if (shape?.Points == null || shape.Points.Count < 2) continue;

                bool isSelectedShape = isSelected && si == _selectedShapeIndex;
                Handles.color = isSelectedShape ? SelectedRegionColor : baseColor;
                for (int p = 0; p < shape.Points.Count; p++)
                {
                    Vector3 a = map.ToWorld(region, shape.Points[p]);
                    Vector3 b = map.ToWorld(region, shape.Points[(p + 1) % shape.Points.Count]);
                    Handles.DrawLine(a, b, 3f);
                }

                Vector3 shapePosition = map.ToWorld(region, MapNavGeometry.AveragePoint(shape.Points));
                float shapeSelectSize = HandleUtility.GetHandleSize(shapePosition) * 0.12f;
                Handles.Label(shapePosition, $"S{si}");
                Handles.color = isSelectedShape ? SelectedVertexHandleColor : ShapeSelectColor;
                if (Handles.Button(shapePosition, Quaternion.identity, shapeSelectSize, shapeSelectSize * 1.4f, Handles.CubeHandleCap))
                {
                    SetSelection(EditSpace.Region, i, si, 0, 0, -1);
                    Repaint();
                }
            }

            Bounds bounds = region.GetLocalBounds();
            Vector3 labelPosition = map.transform.TransformPoint(bounds.center);
            Handles.Label(labelPosition, region.DisplayName);

            float selectSize = HandleUtility.GetHandleSize(labelPosition) * 0.12f;
            if (Handles.Button(labelPosition, Quaternion.identity, selectSize, selectSize * 1.5f, Handles.CubeHandleCap))
            {
                _editSpace = EditSpace.Region;
                SelectSpace(i);
                Repaint();
            }
        }
    }

    private void DrawTransitions(MapNavigationAuthoring map)
    {
        for (int i = 0; i < map.Transitions.Count; i++)
        {
            MapNavTransition transition = map.Transitions[i];
            if (transition == null || transition.Points.Count < 2)
                continue;
            if (!ShouldDrawTransition(map, transition))
                continue;

            Handles.color = _editSpace == EditSpace.Transition && i == _selectedSpaceIndex
                ? SelectedTransitionColor
                : GetTransitionColor(transition);

            for (int p = 0; p < transition.Points.Count; p++)
            {
                Vector3 a = map.ToWorld(transition, transition.Points[p]);
                Vector3 b = map.ToWorld(transition, transition.Points[(p + 1) % transition.Points.Count]);
                Handles.DrawDottedLine(a, b, 4f);
            }

            Bounds bounds = transition.GetLocalBounds();
            Vector3 labelPosition = map.transform.TransformPoint(bounds.center);
            Handles.Label(labelPosition, transition.DisplayName);

            float selectSize = HandleUtility.GetHandleSize(labelPosition) * 0.12f;
            if (Handles.Button(labelPosition, Quaternion.identity, selectSize, selectSize * 1.5f, Handles.CubeHandleCap))
            {
                _editSpace = EditSpace.Transition;
                SelectSpace(i);
                Repaint();
            }
        }
    }

    private void DrawObstacles(MapNavigationAuthoring map)
    {
        for (int regionIndex = 0; regionIndex < map.Regions.Count; regionIndex++)
        {
            MapNavRegion region = map.Regions[regionIndex];
            if (region == null || region.Obstacles == null)
                continue;
            if (!ShouldDrawRegion(region))
                continue;

            for (int obstacleIndex = 0; obstacleIndex < region.Obstacles.Count; obstacleIndex++)
            {
                MapNavObstacle obstacle = region.Obstacles[obstacleIndex];
                if (obstacle == null || obstacle.Points.Count < 2)
                    continue;

                Handles.color = _editSpace == EditSpace.Obstacle
                    && regionIndex == _selectedSpaceIndex
                    && obstacleIndex == _selectedObstacleIndex
                        ? SelectedObstacleColor
                        : ObstacleColor;

                for (int p = 0; p < obstacle.Points.Count; p++)
                {
                    Vector3 a = map.ToWorld(region, obstacle.Points[p]);
                    Vector3 b = map.ToWorld(region, obstacle.Points[(p + 1) % obstacle.Points.Count]);
                    Handles.DrawDottedLine(a, b, 4f);
                }

                Vector3 labelPosition = map.ToWorld(region, MapNavGeometry.AveragePoint(obstacle.Points));
                Handles.Label(labelPosition, $"Obstacle {obstacleIndex}");

                float selectSize = HandleUtility.GetHandleSize(labelPosition) * 0.12f;
                if (Handles.Button(labelPosition, Quaternion.identity, selectSize, selectSize * 1.5f, Handles.CubeHandleCap))
                {
                    _editSpace = EditSpace.Obstacle;
                    SelectSpace(regionIndex);
                    SelectObstacle(obstacleIndex);
                    Repaint();
                }
            }
        }
    }

    private void EditSelectedRegion(MapNavigationAuthoring map)
    {
        if (_selectedSpaceIndex < 0 || _selectedSpaceIndex >= map.Regions.Count)
            return;

        MapNavRegion region = map.Regions[_selectedSpaceIndex];
        if (region == null)
            return;

        MapNavPolygon shape = GetSelectedShape(region);
        if (shape == null) return;

        if (_selectedPointIndex < 0 || _selectedPointIndex >= shape.Points.Count)
            _selectedPointIndex = shape.Points.Count > 0 ? 0 : -1;

        DrawPointSelectors(map, region, shape);
        DrawRegionEdgeHandles(map, region, shape);
    }

    private void EditSelectedTransition(MapNavigationAuthoring map)
    {
        if (_selectedSpaceIndex < 0 || _selectedSpaceIndex >= map.Transitions.Count)
            return;

        MapNavTransition transition = map.Transitions[_selectedSpaceIndex];
        if (transition == null)
            return;

        if (_selectedPointIndex < 0 || _selectedPointIndex >= transition.Points.Count)
            _selectedPointIndex = transition.Points.Count > 0 ? 0 : -1;

        DrawTransitionHeightHandles(map, transition);
        DrawTransitionPointSelectors(map, transition);
        DrawTransitionEdgeHandles(map, transition);
    }

    private void EditSelectedObstacle(MapNavigationAuthoring map)
    {
        if (!TryGetSelectedObstacle(map, out MapNavRegion region, out MapNavObstacle obstacle))
            return;

        if (_selectedPointIndex < 0 || _selectedPointIndex >= obstacle.Points.Count)
            _selectedPointIndex = obstacle.Points.Count > 0 ? 0 : -1;

        DrawObstaclePointSelectors(map, region, obstacle);
        DrawObstacleEdgeHandles(map, region, obstacle);
    }

    private void DrawTransitionHeightHandles(MapNavigationAuthoring map, MapNavTransition transition)
    {
        if (transition.Points.Count == 0)
            return;

        ComputeTransitionEndpointCenters(transition, out Vector2 fromCenter, out Vector2 toCenter);

        Vector3 fromWorld = map.transform.TransformPoint(new Vector3(fromCenter.x, transition.FromHeight, fromCenter.y));
        Vector3 toWorld = map.transform.TransformPoint(new Vector3(toCenter.x, transition.ToHeight, toCenter.y));

        Handles.color = new Color(0.3f, 0.9f, 1f);
        Handles.DrawDottedLine(fromWorld, toWorld, 3f);

        float handleSize = HandleUtility.GetHandleSize(fromWorld) * 0.12f;
        DrawHeightHandle(map, transition, fromWorld, true, handleSize);
        DrawHeightHandle(map, transition, toWorld, false, handleSize);

        Handles.Label(fromWorld, $"From {transition.FromHeight:0.##}");
        Handles.Label(toWorld, $"To {transition.ToHeight:0.##}");
    }

    private static void DrawHeightHandle(MapNavigationAuthoring map, MapNavTransition transition, Vector3 worldPoint, bool isFromHeight, float handleSize)
    {
        Vector3 upEnd = worldPoint + (Vector3.up * HandleUtility.GetHandleSize(worldPoint) * 0.65f);
        Handles.DrawLine(worldPoint, upEnd);

        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.FreeMoveHandle(
            upEnd,
            handleSize,
            Vector3.up * 0.05f,
            Handles.SphereHandleCap
        );

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(map, isFromHeight ? "Edit Transition From Height" : "Edit Transition To Height");
            Vector3 local = map.transform.InverseTransformPoint(moved - (Vector3.up * HandleUtility.GetHandleSize(worldPoint) * 0.65f));

            if (isFromHeight)
                transition.FromHeight = local.y;
            else
                transition.ToHeight = local.y;

            map.RebuildRuntimeData();
            EditorUtility.SetDirty(map);
        }
    }

    private void DrawSelectedTransitionHeightFields(MapNavigationAuthoring map)
    {
        if (_selectedSpaceIndex < 0 || _selectedSpaceIndex >= map.Transitions.Count)
            return;

        MapNavTransition transition = map.Transitions[_selectedSpaceIndex];
        if (transition == null)
            return;

        EditorGUILayout.Space(4f);
        EditorGUI.BeginChangeCheck();
        float fromHeight = EditorGUILayout.FloatField("From Height", transition.FromHeight);
        float toHeight = EditorGUILayout.FloatField("To Height", transition.ToHeight);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(map, "Edit Transition Heights");
            transition.FromHeight = fromHeight;
            transition.ToHeight = toHeight;
            map.RebuildRuntimeData();
            EditorUtility.SetDirty(map);
            SceneView.RepaintAll();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("-0.1"))
                OffsetTransitionHeights(map, transition, -0.1f);

            if (GUILayout.Button("+0.1"))
                OffsetTransitionHeights(map, transition, 0.1f);

            if (GUILayout.Button("Level"))
                SetTransitionToLevel(map, transition);
        }
    }

    private static void OffsetTransitionHeights(MapNavigationAuthoring map, MapNavTransition transition, float offset)
    {
        Undo.RecordObject(map, "Offset Transition Heights");
        transition.FromHeight += offset;
        transition.ToHeight += offset;
        map.RebuildRuntimeData();
        EditorUtility.SetDirty(map);
        SceneView.RepaintAll();
    }

    private static void SetTransitionToLevel(MapNavigationAuthoring map, MapNavTransition transition)
    {
        Undo.RecordObject(map, "Level Transition Heights");
        float height = Mathf.Min(transition.FromHeight, transition.ToHeight);
        transition.FromHeight = height;
        transition.ToHeight = height;
        map.RebuildRuntimeData();
        EditorUtility.SetDirty(map);
        SceneView.RepaintAll();
    }

    private void SelectSpace(int spaceIndex)
    {
        if (_selectedSpaceIndex == spaceIndex)
            return;

        _selectedSpaceIndex = spaceIndex;
        _selectedShapeIndex = 0;
        _selectedObstacleIndex = 0;
        _selectedPointIndex = 0;
        _selectedEdgeIndex = -1;
        SceneView.RepaintAll();
    }

    private void SelectObstacle(int obstacleIndex)
    {
        if (_selectedObstacleIndex == obstacleIndex)
            return;

        _selectedObstacleIndex = obstacleIndex;
        _selectedPointIndex = 0;
        _selectedEdgeIndex = -1;
        SceneView.RepaintAll();
    }

    private void DrawPointSelectors(MapNavigationAuthoring map, MapNavRegion region, MapNavPolygon shape)
    {
        Color previous = Handles.color;

        for (int i = 0; i < shape.Points.Count; i++)
        {
            Vector3 worldPoint = map.ToWorld(region, shape.Points[i]);
            bool isSelected = i == _selectedPointIndex;
            float size = HandleUtility.GetHandleSize(worldPoint) * (isSelected ? 0.115f : 0.085f);
            Handles.color = isSelected ? SelectedVertexHandleColor : VertexHandleColor;

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.FreeMoveHandle(worldPoint, size, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                moved = SnapWorldPoint(map, moved, shape, i);
                Undo.RecordObject(map, "Move Map Navigation Region Point");
                _selectedPointIndex = i;
                _selectedEdgeIndex = -1;
                Vector3 local = map.transform.InverseTransformPoint(moved);
                shape.Points[i] = new Vector2(local.x, local.z);
                map.RebuildRuntimeData();
                EditorUtility.SetDirty(map);
            }
        }

        Handles.color = previous;
    }

    private void DrawTransitionPointSelectors(MapNavigationAuthoring map, MapNavTransition transition)
    {
        Color previous = Handles.color;

        for (int i = 0; i < transition.Points.Count; i++)
        {
            Vector3 worldPoint = map.ToWorld(transition, transition.Points[i]);
            bool isSelected = i == _selectedPointIndex;
            float size = HandleUtility.GetHandleSize(worldPoint) * (isSelected ? 0.115f : 0.085f);
            Handles.color = isSelected ? SelectedVertexHandleColor : VertexHandleColor;

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.FreeMoveHandle(worldPoint, size, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                moved = SnapWorldPoint(map, moved, null, -1);
                Undo.RecordObject(map, "Move Map Navigation Transition Point");
                _selectedPointIndex = i;
                _selectedEdgeIndex = -1;
                Vector3 local = map.transform.InverseTransformPoint(moved);
                transition.Points[i] = new Vector2(local.x, local.z);
                map.RebuildRuntimeData();
                EditorUtility.SetDirty(map);
            }
        }

        Handles.color = previous;
    }

    private void DrawObstaclePointSelectors(MapNavigationAuthoring map, MapNavRegion region, MapNavObstacle obstacle)
    {
        Color previous = Handles.color;

        for (int i = 0; i < obstacle.Points.Count; i++)
        {
            Vector3 worldPoint = map.ToWorld(region, obstacle.Points[i]);
            bool isSelected = i == _selectedPointIndex;
            float size = HandleUtility.GetHandleSize(worldPoint) * (isSelected ? 0.115f : 0.085f);
            Handles.color = isSelected ? SelectedVertexHandleColor : VertexHandleColor;

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.FreeMoveHandle(worldPoint, size, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                moved = SnapWorldPoint(map, moved, null, -1);
                Undo.RecordObject(map, "Move Map Navigation Obstacle Point");
                _selectedPointIndex = i;
                _selectedEdgeIndex = -1;
                Vector3 local = map.transform.InverseTransformPoint(moved);
                obstacle.Points[i] = new Vector2(local.x, local.z);
                map.RebuildRuntimeData();
                EditorUtility.SetDirty(map);
            }
        }

        Handles.color = previous;
    }

    private void DrawRegionEdgeHandles(MapNavigationAuthoring map, MapNavRegion region, MapNavPolygon shape)
    {
        DrawEdgeHandles(
            map,
            shape.Points,
            point => map.ToWorld(region, point),
            "Move Map Navigation Region Edge"
        );
    }

    private void DrawTransitionEdgeHandles(MapNavigationAuthoring map, MapNavTransition transition)
    {
        DrawEdgeHandles(
            map,
            transition.Points,
            point => map.ToWorld(transition, point),
            "Move Map Navigation Transition Edge"
        );
    }

    private void DrawObstacleEdgeHandles(MapNavigationAuthoring map, MapNavRegion region, MapNavObstacle obstacle)
    {
        DrawEdgeHandles(
            map,
            obstacle.Points,
            point => map.ToWorld(region, point),
            "Move Map Navigation Obstacle Edge"
        );
    }

    private void DrawEdgeHandles(
        MapNavigationAuthoring map,
        List<Vector2> points,
        System.Func<Vector2, Vector3> toWorld,
        string undoName)
    {
        if (points == null || points.Count < 2)
            return;

        Color previous = Handles.color;

        for (int i = 0, previousIndex = points.Count - 1; i < points.Count; previousIndex = i++)
        {
            Vector2 localA = points[previousIndex];
            Vector2 localB = points[i];
            Vector3 worldA = toWorld(localA);
            Vector3 worldB = toWorld(localB);
            Vector3 midpoint = (worldA + worldB) * 0.5f;
            bool isSelected = i == _selectedEdgeIndex;
            float size = HandleUtility.GetHandleSize(midpoint) * (isSelected ? 0.22f : 0.17f);

            // 변을 그 변에 수직인 방향(법선)으로만 이동시킨다 — 사각형 등이 평행을 유지하며 안팎으로만
            // 조절되고 대각선으로 찌그러지지 않게 한다. 축 정렬 변이면 정확히 x 또는 z 한 축으로만 움직인다.
            Vector3 worldEdge = worldB - worldA;
            Vector3 edgeNormal = new Vector3(-worldEdge.z, 0f, worldEdge.x);
            edgeNormal = edgeNormal.sqrMagnitude > 1e-6f ? edgeNormal.normalized : Vector3.forward;
            Handles.color = isSelected ? SelectedEdgeHandleColor : EdgeHandleColor;

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.Slider(
                midpoint,
                edgeNormal,
                size,
                Handles.CubeHandleCap,
                0f
            );

            if (EditorGUI.EndChangeCheck())
            {
                Vector3 localMidpoint = map.transform.InverseTransformPoint(midpoint);
                Vector3 localMoved = map.transform.InverseTransformPoint(moved);
                Vector2 delta = new(localMoved.x - localMidpoint.x, localMoved.z - localMidpoint.z);
                delta = SnapEdgeDelta(map, points, i, delta);

                if (delta.sqrMagnitude > 1e-6f)
                {
                    Undo.RecordObject(map, undoName);
                    _selectedEdgeIndex = i;
                    _selectedPointIndex = i;
                    points[previousIndex] += delta;
                    points[i] += delta;
                    map.RebuildRuntimeData();
                    EditorUtility.SetDirty(map);
                }
            }
        }

        Handles.color = previous;
    }

    private Vector2 SnapEdgeDelta(MapNavigationAuthoring map, List<Vector2> points, int edgeEndIndex, Vector2 delta)
    {
        if (!_snapEnabled || points == null || points.Count < 2 || delta.sqrMagnitude <= 1e-6f)
            return delta;

        int edgeStartIndex = edgeEndIndex == 0 ? points.Count - 1 : edgeEndIndex - 1;
        Vector2 originalA = points[edgeStartIndex];
        Vector2 originalB = points[edgeEndIndex];
        Vector2 movedA = originalA + delta;
        Vector2 movedB = originalB + delta;
        Vector2 edge = movedB - movedA;
        if (edge.sqrMagnitude <= 1e-6f)
            return delta;

        Vector2 tangent = edge.normalized;
        Vector2 normal = new(-tangent.y, tangent.x);
        if (Vector2.Dot(normal, delta) < 0f)
            normal = -normal;

        Vector2 movedMidpoint = (movedA + movedB) * 0.5f;
        Vector3 movedWorld = map.transform.TransformPoint(new Vector3(movedMidpoint.x, 0f, movedMidpoint.y));
        float scale = Mathf.Max(Mathf.Abs(map.transform.lossyScale.x), Mathf.Abs(map.transform.lossyScale.z));
        scale = Mathf.Max(scale, 0.0001f);
        float threshold = HandleUtility.GetHandleSize(movedWorld) * 0.22f / scale;
        float bestAbsDistance = threshold;
        float bestDistance = 0f;

        VisitSnapEdges(map, points, edgeEndIndex, movedA, movedB, tangent, normal, threshold, ref bestAbsDistance, ref bestDistance);
        return bestAbsDistance < threshold ? delta + normal * bestDistance : delta;
    }

    private static void VisitSnapEdges(
        MapNavigationAuthoring map,
        List<Vector2> sourcePoints,
        int sourceEdgeEndIndex,
        Vector2 movedA,
        Vector2 movedB,
        Vector2 tangent,
        Vector2 normal,
        float threshold,
        ref float bestAbsDistance,
        ref float bestDistance)
    {
        for (int r = 0; r < map.Regions.Count; r++)
        {
            MapNavRegion region = map.Regions[r];
            if (region?.Shapes != null)
            {
                for (int s = 0; s < region.Shapes.Count; s++)
                    CheckSnapEdges(region.Shapes[s]?.Points, sourcePoints, sourceEdgeEndIndex, movedA, movedB, tangent, normal, threshold, ref bestAbsDistance, ref bestDistance);
            }

            if (region?.Obstacles == null)
                continue;

            for (int o = 0; o < region.Obstacles.Count; o++)
                CheckSnapEdges(region.Obstacles[o]?.Points, sourcePoints, sourceEdgeEndIndex, movedA, movedB, tangent, normal, threshold, ref bestAbsDistance, ref bestDistance);
        }

        for (int t = 0; t < map.Transitions.Count; t++)
            CheckSnapEdges(map.Transitions[t]?.Points, sourcePoints, sourceEdgeEndIndex, movedA, movedB, tangent, normal, threshold, ref bestAbsDistance, ref bestDistance);
    }

    private static void CheckSnapEdges(
        List<Vector2> candidatePoints,
        List<Vector2> sourcePoints,
        int sourceEdgeEndIndex,
        Vector2 movedA,
        Vector2 movedB,
        Vector2 tangent,
        Vector2 normal,
        float threshold,
        ref float bestAbsDistance,
        ref float bestDistance)
    {
        if (candidatePoints == null || candidatePoints.Count < 2)
            return;

        for (int i = 0, previousIndex = candidatePoints.Count - 1; i < candidatePoints.Count; previousIndex = i++)
        {
            if (ReferenceEquals(candidatePoints, sourcePoints) && i == sourceEdgeEndIndex)
                continue;

            Vector2 targetA = candidatePoints[previousIndex];
            Vector2 targetB = candidatePoints[i];
            Vector2 targetEdge = targetB - targetA;
            if (targetEdge.sqrMagnitude <= 1e-6f)
                continue;

            Vector2 targetTangent = targetEdge.normalized;
            if (Mathf.Abs(Vector2.Dot(tangent, targetTangent)) < 0.94f)
                continue;

            float movedMin = Mathf.Min(Vector2.Dot(movedA, tangent), Vector2.Dot(movedB, tangent));
            float movedMax = Mathf.Max(Vector2.Dot(movedA, tangent), Vector2.Dot(movedB, tangent));
            float targetMin = Mathf.Min(Vector2.Dot(targetA, tangent), Vector2.Dot(targetB, tangent));
            float targetMax = Mathf.Max(Vector2.Dot(targetA, tangent), Vector2.Dot(targetB, tangent));
            if (Mathf.Min(movedMax, targetMax) + threshold < Mathf.Max(movedMin, targetMin))
                continue;

            Vector2 movedMidpoint = (movedA + movedB) * 0.5f;
            Vector2 targetMidpoint = (targetA + targetB) * 0.5f;
            float distance = Vector2.Dot(targetMidpoint - movedMidpoint, normal);
            float absDistance = Mathf.Abs(distance);
            if (absDistance < bestAbsDistance)
            {
                bestAbsDistance = absDistance;
                bestDistance = distance;
            }
        }
    }

    private int GetMaxSpaceIndex(MapNavigationAuthoring map)
    {
        return _editSpace switch
        {
            EditSpace.Region => map.Regions.Count - 1,
            EditSpace.Transition => map.Transitions.Count - 1,
            _ => map.Regions.Count - 1
        };
    }

    private int GetMaxShapeIndex(MapNavigationAuthoring map)
    {
        if (_selectedSpaceIndex < 0 || _selectedSpaceIndex >= map.Regions.Count) return -1;
        MapNavRegion region = map.Regions[_selectedSpaceIndex];
        return region?.Shapes != null ? region.Shapes.Count - 1 : -1;
    }

    private MapNavPolygon GetSelectedShape(MapNavRegion region)
    {
        if (region?.Shapes == null || region.Shapes.Count == 0) return null;
        int idx = Mathf.Clamp(_selectedShapeIndex, 0, region.Shapes.Count - 1);
        return region.Shapes[idx];
    }

    private int GetMaxObstacleIndex(MapNavigationAuthoring map)
    {
        if (_selectedSpaceIndex < 0 || _selectedSpaceIndex >= map.Regions.Count)
            return -1;

        MapNavRegion region = map.Regions[_selectedSpaceIndex];
        return region?.Obstacles != null ? region.Obstacles.Count - 1 : -1;
    }

    private int GetMaxPointIndex(MapNavigationAuthoring map)
    {
        if (_editSpace == EditSpace.Region)
        {
            if (_selectedSpaceIndex < 0 || _selectedSpaceIndex >= map.Regions.Count)
                return -1;

            MapNavRegion region = map.Regions[_selectedSpaceIndex];
            MapNavPolygon shape = GetSelectedShape(region);
            return shape != null ? shape.Points.Count - 1 : -1;
        }

        if (_editSpace == EditSpace.Transition)
        {
            if (_selectedSpaceIndex < 0 || _selectedSpaceIndex >= map.Transitions.Count)
                return -1;

            MapNavTransition transition = map.Transitions[_selectedSpaceIndex];
            return transition != null ? transition.Points.Count - 1 : -1;
        }

        if (!TryGetSelectedObstacle(map, out _, out MapNavObstacle obstacle))
            return -1;

        return obstacle != null ? obstacle.Points.Count - 1 : -1;
    }

    private int GetMaxEdgeIndex(MapNavigationAuthoring map)
    {
        int maxPointIndex = GetMaxPointIndex(map);
        return maxPointIndex >= 1 ? maxPointIndex : -1;
    }

    private bool TryGetSelectedObstacle(MapNavigationAuthoring map, out MapNavRegion region, out MapNavObstacle obstacle)
    {
        region = null;
        obstacle = null;

        if (_selectedSpaceIndex < 0 || _selectedSpaceIndex >= map.Regions.Count)
            return false;

        region = map.Regions[_selectedSpaceIndex];
        if (region == null || region.Obstacles == null)
            return false;

        if (_selectedObstacleIndex < 0 || _selectedObstacleIndex >= region.Obstacles.Count)
            return false;

        obstacle = region.Obstacles[_selectedObstacleIndex];
        return obstacle != null;
    }

    private void AddFlatRegion(MapNavigationAuthoring map)
    {
        Vector2 center = GetSceneViewCenterLocal(map, GetSelectedRegionHeight());
        var shape = new MapNavPolygon();
        AddRectPoints(shape.Points, center, new Vector2(1f, 1f));

        AddRegionSerialized(map, "Add Map Navigation Region", shape, GetSelectedRegionHeight());
    }

    private void AddCircleRegion(MapNavigationAuthoring map, int segments, float radius)
    {
        Vector2 center = GetSceneViewCenterLocal(map, GetSelectedRegionHeight());
        var shape = new MapNavPolygon();
        for (int i = 0; i < segments; i++)
        {
            float angle = i * 2f * Mathf.PI / segments;
            shape.Points.Add(center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius));
        }

        AddRegionSerialized(map, "Add Circle Region", shape, GetSelectedRegionHeight());
    }

    private void AddRegionSerialized(MapNavigationAuthoring map, string undoName, MapNavPolygon shape, float height)
    {
        Undo.RecordObject(map, undoName);

        serializedObject.Update();
        SerializedProperty regions = serializedObject.FindProperty("regions");
        int newIndex = regions.arraySize;
        regions.InsertArrayElementAtIndex(newIndex);

        SerializedProperty region = regions.GetArrayElementAtIndex(newIndex);
        region.FindPropertyRelative("Id").intValue = map.GetNextRegionId();
        region.FindPropertyRelative("Height").floatValue = height;
        region.FindPropertyRelative("Cost").floatValue = 1f;

        SerializedProperty shapes = region.FindPropertyRelative("Shapes");
        shapes.arraySize = 1;
        SerializedProperty shapeProperty = shapes.GetArrayElementAtIndex(0);
        SerializedProperty points = shapeProperty.FindPropertyRelative("Points");
        points.arraySize = shape.Points.Count;
        for (int i = 0; i < shape.Points.Count; i++)
            points.GetArrayElementAtIndex(i).vector2Value = shape.Points[i];

        SerializedProperty obstacles = region.FindPropertyRelative("Obstacles");
        if (obstacles != null)
            obstacles.arraySize = 0;

        serializedObject.ApplyModifiedProperties();
        _editSpace = EditSpace.Region;
        _selectedSpaceIndex = newIndex;
        _selectedShapeIndex = 0;
        _selectedObstacleIndex = 0;
        _selectedPointIndex = 0;
        _selectedEdgeIndex = -1;
        map.RebuildRuntimeData();
        EditorUtility.SetDirty(map);
        SceneView.RepaintAll();
    }

    private void AddShape(MapNavigationAuthoring map)
    {
        if (_selectedSpaceIndex < 0 || _selectedSpaceIndex >= map.Regions.Count) return;

        MapNavRegion region = map.Regions[_selectedSpaceIndex];
        if (region == null) return;

        Undo.RecordObject(map, "Add Shape to Region");

        Vector2 center = GetRegionCenter(region);
        var shape = new MapNavPolygon();
        AddRectPoints(shape.Points, center, new Vector2(1f, 1f));

        region.Shapes.Add(shape);
        _selectedShapeIndex = region.Shapes.Count - 1;
        _selectedPointIndex = 0;
        _selectedEdgeIndex = -1;
        map.RebuildRuntimeData();
        EditorUtility.SetDirty(map);
        SceneView.RepaintAll();
    }

    private void AddTransition(MapNavigationAuthoring map, MapNavTransitionType type)
    {
        Undo.RecordObject(map, "Add Map Navigation Transition");

        int id = map.GetNextTransitionId();
        Vector2 center = GetSceneViewCenterLocal(map, GetSelectedRegionHeight());
        map.AddTransition(new MapNavTransition
        {
            Id = id,
            Type = type,
            FromHeight = GetSelectedRegionHeight(),
            ToHeight = type == MapNavTransitionType.Edge || type == MapNavTransitionType.Door ? GetSelectedRegionHeight() : GetSelectedRegionHeight() + 3f,
            CanStopInside = true,
            CanFightInside = false,
            Points =
            {
                center + new Vector2(-0.5f, -2f),
                center + new Vector2(-0.5f, 2f),
                center + new Vector2(0.5f, 2f),
                center + new Vector2(0.5f, -2f)
            }
        });

        _editSpace = EditSpace.Transition;
        _selectedSpaceIndex = map.Transitions.Count - 1;
        _selectedShapeIndex = 0;
        _selectedObstacleIndex = 0;
        _selectedPointIndex = 0;
        _selectedEdgeIndex = -1;
        EditorUtility.SetDirty(map);
        SceneView.RepaintAll();
    }

    private void AddAutoEdgeTransition(MapNavigationAuthoring map)
    {
        if (!TryGetSelectedRegionEdge(map, out MapNavRegion fromRegion, out int fromRegionIndex, out Vector2 fromA, out Vector2 fromB))
        {
            Debug.LogWarning("[MapNav] Select a region edge before connecting.", map);
            return;
        }

        if (!TryFindNearestRegionEdge(map, fromRegionIndex, fromA, fromB, out MapNavRegion toRegion, out Vector2 toA, out Vector2 toB))
        {
            Debug.LogWarning("[MapNav] No other region edge found to connect.", map);
            return;
        }

        OrientTargetEdge(fromA, fromB, ref toA, ref toB);

        Vector2 fromMid = (fromA + fromB) * 0.5f;
        Vector2 toMid = (toA + toB) * 0.5f;
        Vector2 upDirection = toMid - fromMid;
        float distance = upDirection.magnitude;
        if (distance > 0.0001f)
            upDirection /= distance;
        else
            upDirection = GetEdgeNormal(fromA, fromB);

        const float minWidth = 0.35f;
        if (distance < minWidth)
        {
            Vector2 halfOffset = upDirection * (minWidth * 0.5f);
            fromA -= halfOffset;
            fromB -= halfOffset;
            toA = fromA + halfOffset * 2f;
            toB = fromB + halfOffset * 2f;
        }

        MapNavTransitionType type = Mathf.Abs(fromRegion.Height - toRegion.Height) <= 0.05f
            ? MapNavTransitionType.Edge
            : MapNavTransitionType.Stair;

        Undo.RecordObject(map, "Auto Connect Map Navigation Transition");
        map.AddTransition(new MapNavTransition
        {
            Id = map.GetNextTransitionId(),
            FromRegionId = fromRegion.Id,
            ToRegionId = toRegion.Id,
            Type = type,
            FromHeight = fromRegion.Height,
            ToHeight = toRegion.Height,
            UpDirection = upDirection,
            CanStopInside = true,
            CanFightInside = false,
            Bidirectional = true,
            Enabled = true,
            Points = { fromA, fromB, toB, toA }
        });

        _editSpace = EditSpace.Transition;
        _selectedSpaceIndex = map.Transitions.Count - 1;
        _selectedShapeIndex = 0;
        _selectedObstacleIndex = 0;
        _selectedPointIndex = 0;
        _selectedEdgeIndex = -1;
        map.RebuildRuntimeData();
        EditorUtility.SetDirty(map);
        SceneView.RepaintAll();
    }

    private bool TryGetSelectedRegionEdge(MapNavigationAuthoring map, out MapNavRegion region, out int regionIndex, out Vector2 a, out Vector2 b)
    {
        region = null;
        regionIndex = -1;
        a = default;
        b = default;

        if (_editSpace != EditSpace.Region)
            return false;

        if (_selectedSpaceIndex < 0 || _selectedSpaceIndex >= map.Regions.Count)
            return false;

        region = map.Regions[_selectedSpaceIndex];
        MapNavPolygon shape = GetSelectedShape(region);
        if (region == null || shape?.Points == null || shape.Points.Count < 2)
            return false;

        int edgeEndIndex = _selectedEdgeIndex >= 0 ? _selectedEdgeIndex : Mathf.Clamp(_selectedPointIndex, 0, shape.Points.Count - 1);
        int edgeStartIndex = edgeEndIndex == 0 ? shape.Points.Count - 1 : edgeEndIndex - 1;
        a = shape.Points[edgeStartIndex];
        b = shape.Points[edgeEndIndex];
        regionIndex = _selectedSpaceIndex;
        return true;
    }

    private static bool TryFindNearestRegionEdge(
        MapNavigationAuthoring map,
        int excludeRegionIndex,
        Vector2 sourceA,
        Vector2 sourceB,
        out MapNavRegion bestRegion,
        out Vector2 bestA,
        out Vector2 bestB)
    {
        bestRegion = null;
        bestA = default;
        bestB = default;
        float bestDistance = float.MaxValue;

        for (int r = 0; r < map.Regions.Count; r++)
        {
            if (r == excludeRegionIndex)
                continue;

            MapNavRegion region = map.Regions[r];
            if (region?.Shapes == null)
                continue;

            for (int s = 0; s < region.Shapes.Count; s++)
            {
                List<Vector2> points = region.Shapes[s]?.Points;
                if (points == null || points.Count < 2)
                    continue;

                for (int i = 0, previousIndex = points.Count - 1; i < points.Count; previousIndex = i++)
                {
                    Vector2 a = points[previousIndex];
                    Vector2 b = points[i];
                    float distance = SegmentDistance(sourceA, sourceB, a, b);
                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    bestRegion = region;
                    bestA = a;
                    bestB = b;
                }
            }
        }

        return bestRegion != null;
    }

    private static void OrientTargetEdge(Vector2 fromA, Vector2 fromB, ref Vector2 toA, ref Vector2 toB)
    {
        float direct = (fromA - toA).sqrMagnitude + (fromB - toB).sqrMagnitude;
        float swapped = (fromA - toB).sqrMagnitude + (fromB - toA).sqrMagnitude;
        if (swapped < direct)
            (toA, toB) = (toB, toA);
    }

    private static Vector2 GetEdgeNormal(Vector2 a, Vector2 b)
    {
        Vector2 edge = b - a;
        if (edge.sqrMagnitude <= 1e-6f)
            return Vector2.up;

        edge.Normalize();
        return new Vector2(-edge.y, edge.x);
    }

    private static float SegmentDistance(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
    {
        if (MapNavGeometry.SegmentsIntersect(a0, a1, b0, b1))
            return 0f;

        float d0 = (MapNavGeometry.ClosestPointOnSegment(a0, b0, b1) - a0).sqrMagnitude;
        float d1 = (MapNavGeometry.ClosestPointOnSegment(a1, b0, b1) - a1).sqrMagnitude;
        float d2 = (MapNavGeometry.ClosestPointOnSegment(b0, a0, a1) - b0).sqrMagnitude;
        float d3 = (MapNavGeometry.ClosestPointOnSegment(b1, a0, a1) - b1).sqrMagnitude;
        return Mathf.Sqrt(Mathf.Min(Mathf.Min(d0, d1), Mathf.Min(d2, d3)));
    }

    private void AddObstacle(MapNavigationAuthoring map)
    {
        Undo.RecordObject(map, "Add Map Navigation Obstacle");

        if (map.Regions.Count == 0)
            return;

        int regionIndex = Mathf.Clamp(_selectedSpaceIndex, 0, map.Regions.Count - 1);
        MapNavRegion region = map.Regions[regionIndex];
        Vector2 center = GetRegionCenter(region);
        region.Obstacles.Add(new MapNavObstacle
        {
            Points =
            {
                center + new Vector2(-0.5f, -0.5f),
                center + new Vector2(-0.5f, 0.5f),
                center + new Vector2(0.5f, 0.5f),
                center + new Vector2(0.5f, -0.5f)
            }
        });

        _editSpace = EditSpace.Obstacle;
        _selectedSpaceIndex = regionIndex;
        _selectedObstacleIndex = region.Obstacles.Count - 1;
        _selectedPointIndex = 0;
        _selectedEdgeIndex = -1;
        map.RebuildRuntimeData();
        EditorUtility.SetDirty(map);
        SceneView.RepaintAll();
    }

    private static void AddRectPoints(List<Vector2> points, Vector2 center, Vector2 halfSize)
    {
        points.Add(center + new Vector2(-halfSize.x, -halfSize.y));
        points.Add(center + new Vector2(-halfSize.x, halfSize.y));
        points.Add(center + new Vector2(halfSize.x, halfSize.y));
        points.Add(center + new Vector2(halfSize.x, -halfSize.y));
    }

    private static Vector2 GetRegionCenter(MapNavRegion region)
    {
        if (region == null)
            return Vector2.zero;

        Bounds bounds = region.GetLocalBounds();
        if (bounds.size.sqrMagnitude > 1e-6f)
            return new Vector2(bounds.center.x, bounds.center.z);

        if (region.Shapes != null && region.Shapes.Count > 0 && region.Shapes[0]?.Points != null)
            return MapNavGeometry.AveragePoint(region.Shapes[0].Points);

        return Vector2.zero;
    }

    private Vector2 GetSceneViewCenterLocal(MapNavigationAuthoring map, float localHeight)
    {
        if (_selectedSpaceIndex >= 0 && _selectedSpaceIndex < map.Regions.Count)
            return GetRegionCenter(map.Regions[_selectedSpaceIndex]);

        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            Vector3 local = map.transform.InverseTransformPoint(sceneView.pivot);
            return new Vector2(local.x, local.z);
        }

        return Vector2.zero;
    }

    private static void LogValidation(MapNavigationAuthoring map)
    {
        List<string> results = ValidateAuthoring(map);
        if (results.Count == 0)
        {
            Debug.Log("Map navigation validation passed.", map);
            return;
        }

        for (int i = 0; i < results.Count; i++)
            Debug.LogWarning(results[i], map);
    }

    private static List<string> ValidateAuthoring(MapNavigationAuthoring map)
    {
        List<string> results = new();
        if (map == null) { results.Add("MapNavigationAuthoring is null."); return results; }

        HashSet<int> regionIds = new();
        IReadOnlyList<MapNavRegion> regions = map.Regions;
        for (int i = 0; i < regions.Count; i++)
        {
            MapNavRegion region = regions[i];
            if (region == null) { results.Add($"Region at index {i} is null."); continue; }
            if (region.Id < 0) results.Add($"{region.DisplayName} has invalid id {region.Id}.");
            if (!regionIds.Add(region.Id)) results.Add($"Region id {region.Id} is duplicated.");
            if (region.Shapes == null || region.Shapes.Count == 0) results.Add($"Region {region.Id} needs at least one shape.");
            else for (int si = 0; si < region.Shapes.Count; si++)
            {
                MapNavPolygon shape = region.Shapes[si];
                if (shape == null || shape.Points.Count < 3) results.Add($"Region {region.Id} shape {si} needs at least 3 points.");
                else if (HasSelfIntersection(shape.Points)) results.Add($"Region {region.Id} shape {si} has self intersection.");
            }
            if (region.Cost < 0f) results.Add($"Region {region.Id} must not have negative cost.");
            if (region.Obstacles == null) continue;

            for (int oi = 0; oi < region.Obstacles.Count; oi++)
            {
                MapNavObstacle obstacle = region.Obstacles[oi];
                if (obstacle == null) { results.Add($"Region {region.Id} obstacle at index {oi} is null."); continue; }
                if (obstacle.Points == null || obstacle.Points.Count < 3)
                    results.Add($"Region {region.Id} obstacle {oi} needs at least 3 points.");
                if (HasSelfIntersection(obstacle.Points))
                    results.Add($"Region {region.Id} obstacle {oi} polygon has self intersection.");
                if (obstacle.CornerPadding < 0f)
                    results.Add($"Region {region.Id} obstacle {oi} has negative corner padding.");
            }
        }

        HashSet<int> transitionIds = new();
        IReadOnlyList<MapNavTransition> transitions = map.Transitions;
        for (int i = 0; i < transitions.Count; i++)
        {
            MapNavTransition transition = transitions[i];
            if (transition == null) { results.Add($"Transition at index {i} is null."); continue; }
            if (transition.Id < 0) results.Add($"{transition.DisplayName} has invalid id {transition.Id}.");
            if (!transitionIds.Add(transition.Id)) results.Add($"Transition id {transition.Id} is duplicated.");

            MapNavRegion fromRegion = FindRegion(regions, transition.FromRegionId);
            MapNavRegion toRegion = FindRegion(regions, transition.ToRegionId);
            if (fromRegion == null) results.Add($"Transition {i} has missing FromRegionId {transition.FromRegionId}.");
            if (toRegion == null) results.Add($"Transition {i} has missing ToRegionId {transition.ToRegionId}.");
            if (fromRegion != null && Mathf.Abs(transition.FromHeight - fromRegion.Height) > 0.05f)
                results.Add($"Transition {transition.Id} FromHeight {transition.FromHeight:0.###} != {fromRegion.DisplayName} height {fromRegion.Height:0.###}.");
            if (toRegion != null && Mathf.Abs(transition.ToHeight - toRegion.Height) > 0.05f)
                results.Add($"Transition {transition.Id} ToHeight {transition.ToHeight:0.###} != {toRegion.DisplayName} height {toRegion.Height:0.###}.");

            if (transition.Points == null || transition.Points.Count < 3)
                results.Add($"Transition {transition.Id} needs at least 3 points.");
            if (HasSelfIntersection(transition.Points))
                results.Add($"Transition {transition.Id} polygon has self intersection.");
            if ((transition.Type == MapNavTransitionType.Stair || transition.Type == MapNavTransitionType.Ramp)
                && transition.UpDirection.sqrMagnitude < 0.0001f)
                results.Add($"Transition {transition.Id} is {transition.Type} but has no up direction.");
            if (transition.MinRadius < 0f) results.Add($"Transition {i} has negative MinRadius.");
            if (transition.Cost < 0f) results.Add($"Transition {transition.Id} must not have negative cost.");
        }

        return results;
    }

    private static MapNavRegion FindRegion(IReadOnlyList<MapNavRegion> regions, int id)
    {
        for (int i = 0; i < regions.Count; i++)
            if (regions[i] != null && regions[i].Id == id) return regions[i];
        return null;
    }

    private static bool HasSelfIntersection(IReadOnlyList<Vector2> points)
    {
        if (points == null || points.Count < 4) return false;
        for (int a = 0; a < points.Count; a++)
        {
            int b = (a + 1) % points.Count;
            for (int c = a + 1; c < points.Count; c++)
            {
                int d = (c + 1) % points.Count;
                if (a == c || a == d || b == c || b == d) continue;
                if (MapNavGeometry.SegmentsIntersect(points[a], points[b], points[c], points[d]))
                    return true;
            }
        }
        return false;
    }

    private static void ComputeTransitionEndpointCenters(MapNavTransition transition, out Vector2 fromCenter, out Vector2 toCenter)
    {
        IReadOnlyList<Vector2> points = transition?.Points != null ? transition.Points : System.Array.Empty<Vector2>();
        Vector2 direction = transition != null && transition.UpDirection.sqrMagnitude > 0.0001f
            ? transition.UpDirection.normalized
            : Vector2.up;
        fromCenter = ProjectionExtremeCenter(points, direction, true);
        toCenter = ProjectionExtremeCenter(points, direction, false);
    }

    private static Vector2 ProjectionExtremeCenter(IReadOnlyList<Vector2> points, Vector2 direction, bool useMin)
    {
        if (points == null || points.Count == 0) return Vector2.zero;
        if (points.Count <= 2) return points[0];

        int firstIndex = 0;
        int secondIndex = 1;
        float firstProjection = Vector2.Dot(points[firstIndex], direction);
        float secondProjection = Vector2.Dot(points[secondIndex], direction);
        if (Better(secondProjection, firstProjection, useMin))
        {
            (firstIndex, secondIndex) = (secondIndex, firstIndex);
            (firstProjection, secondProjection) = (secondProjection, firstProjection);
        }

        for (int i = 2; i < points.Count; i++)
        {
            float projected = Vector2.Dot(points[i], direction);
            if (Better(projected, firstProjection, useMin))
            {
                secondIndex = firstIndex; secondProjection = firstProjection;
                firstIndex = i; firstProjection = projected;
                continue;
            }
            if (Better(projected, secondProjection, useMin))
            {
                secondIndex = i; secondProjection = projected;
            }
        }
        return (points[firstIndex] + points[secondIndex]) * 0.5f;

        static bool Better(float candidate, float current, bool min) => min ? candidate < current : candidate > current;
    }

    private static Color GetTransitionColor(MapNavTransition transition)
    {
        return transition.Type switch
        {
            MapNavTransitionType.Stair => new Color(0.35f, 0.65f, 1f),
            MapNavTransitionType.Ramp => new Color(0.55f, 1f, 0.55f),
            MapNavTransitionType.Door => new Color(1f, 0.35f, 0.35f),
            _ => Color.white
        };
    }

}

[CustomPropertyDrawer(typeof(MapNavRegion))]
public sealed class MapNavRegionPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        MapNavigationPropertyDrawerUtility.DrawFoldoutProperty(position, property, GetLabel(property));
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return MapNavigationPropertyDrawerUtility.GetFoldoutPropertyHeight(property);
    }

    private static string GetLabel(SerializedProperty property)
    {
        int id = property.FindPropertyRelative("Id")?.intValue ?? -1;
        float height = property.FindPropertyRelative("Height")?.floatValue ?? 0f;
        int shapeCount = property.FindPropertyRelative("Shapes")?.arraySize ?? 0;
        int obstacleCount = property.FindPropertyRelative("Obstacles")?.arraySize ?? 0;
        return $"Region {id} | H {height:0.##} | Shapes {shapeCount} | Obs {obstacleCount}";
    }
}

[CustomPropertyDrawer(typeof(MapNavPolygon))]
public sealed class MapNavPolygonPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty points = property.FindPropertyRelative("Points");
        string title = $"Shape | P {points?.arraySize ?? 0}";
        MapNavigationPropertyDrawerUtility.DrawSingleListProperty(position, property, points, title);
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        SerializedProperty points = property.FindPropertyRelative("Points");
        return MapNavigationPropertyDrawerUtility.GetSingleListPropertyHeight(property, points);
    }
}

[CustomPropertyDrawer(typeof(MapNavTransition))]
public sealed class MapNavTransitionPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        MapNavigationPropertyDrawerUtility.DrawFoldoutProperty(position, property, GetLabel(property));
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return MapNavigationPropertyDrawerUtility.GetFoldoutPropertyHeight(property);
    }

    private static string GetLabel(SerializedProperty property)
    {
        int id = property.FindPropertyRelative("Id")?.intValue ?? -1;
        int fromRegionId = property.FindPropertyRelative("FromRegionId")?.intValue ?? -1;
        int toRegionId = property.FindPropertyRelative("ToRegionId")?.intValue ?? -1;
        SerializedProperty type = property.FindPropertyRelative("Type");
        string typeName = type != null ? type.enumDisplayNames[type.enumValueIndex] : "Unknown";
        return $"Transition {id} | {typeName} | {fromRegionId} -> {toRegionId}";
    }
}

[CustomPropertyDrawer(typeof(MapNavObstacle))]
public sealed class MapNavObstaclePropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        MapNavigationPropertyDrawerUtility.DrawFoldoutProperty(position, property, GetLabel(property));
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return MapNavigationPropertyDrawerUtility.GetFoldoutPropertyHeight(property);
    }

    private static string GetLabel(SerializedProperty property)
    {
        int pointCount = property.FindPropertyRelative("Points")?.arraySize ?? 0;
        float cornerPadding = property.FindPropertyRelative("CornerPadding")?.floatValue ?? 0f;
        return $"Obstacle | P {pointCount} | Padding {cornerPadding:0.##}";
    }
}

public static class MapNavigationPropertyDrawerUtility
{
    public static void DrawSingleListProperty(Rect position, SerializedProperty property, SerializedProperty listProperty, string label)
    {
        Rect foldoutRect = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);
        if (!property.isExpanded || listProperty == null)
            return;

        EditorGUI.indentLevel++;
        float y = position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        Rect sizeRect = new(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
        int nextSize = Mathf.Max(0, EditorGUI.IntField(sizeRect, "Points", listProperty.arraySize));
        if (nextSize != listProperty.arraySize)
            listProperty.arraySize = nextSize;

        y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        EditorGUI.indentLevel++;
        for (int i = 0; i < listProperty.arraySize; i++)
        {
            SerializedProperty element = listProperty.GetArrayElementAtIndex(i);
            Rect elementRect = new(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(elementRect, element, new GUIContent($"P {i}"));
            y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        }
        EditorGUI.indentLevel -= 2;
    }

    public static float GetSingleListPropertyHeight(SerializedProperty property, SerializedProperty listProperty)
    {
        float height = EditorGUIUtility.singleLineHeight;
        if (!property.isExpanded || listProperty == null)
            return height;

        height += EditorGUIUtility.standardVerticalSpacing + EditorGUIUtility.singleLineHeight;
        height += listProperty.arraySize * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
        return height;
    }

    public static void DrawFoldoutProperty(Rect position, SerializedProperty property, string label)
    {
        Rect foldoutRect = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);
        if (!property.isExpanded)
            return;

        EditorGUI.indentLevel++;
        SerializedProperty iterator = property.Copy();
        SerializedProperty end = iterator.GetEndProperty();
        bool enterChildren = true;
        float y = position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
        {
            enterChildren = false;
            Rect childRect = new(position.x, y, position.width, EditorGUI.GetPropertyHeight(iterator, true));
            EditorGUI.PropertyField(childRect, iterator, true);
            y += childRect.height + EditorGUIUtility.standardVerticalSpacing;
        }

        EditorGUI.indentLevel--;
    }

    public static float GetFoldoutPropertyHeight(SerializedProperty property)
    {
        float height = EditorGUIUtility.singleLineHeight;
        if (!property.isExpanded)
            return height;

        SerializedProperty iterator = property.Copy();
        SerializedProperty end = iterator.GetEndProperty();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
        {
            enterChildren = false;
            height += EditorGUI.GetPropertyHeight(iterator, true) + EditorGUIUtility.standardVerticalSpacing;
        }

        return height;
    }
}
