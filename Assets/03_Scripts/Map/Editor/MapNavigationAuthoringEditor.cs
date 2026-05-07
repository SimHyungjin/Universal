using System.Collections.Generic;
using UnityEditor;
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
    private int _selectedObstacleIndex;
    private int _selectedPointIndex;

    public override void OnInspectorGUI()
    {
        MapNavigationAuthoring map = (MapNavigationAuthoring)target;

        if (map.Regions.Count > 0)
        {
            EditorGUILayout.LabelField("Scene Editing", EditorStyles.boldLabel);
            EditSpace nextEditSpace = (EditSpace)EditorGUILayout.EnumPopup("Edit Space", _editSpace);
            if (nextEditSpace != _editSpace)
            {
                _editSpace = nextEditSpace;
                _selectedSpaceIndex = 0;
                _selectedObstacleIndex = 0;
                _selectedPointIndex = 0;
            }

            int maxSpaceIndex = GetMaxSpaceIndex(map);
            using (new EditorGUI.DisabledScope(maxSpaceIndex < 0))
            {
                int nextSpaceIndex = EditorGUILayout.IntSlider("Space Index", Mathf.Clamp(_selectedSpaceIndex, 0, Mathf.Max(0, maxSpaceIndex)), 0, Mathf.Max(0, maxSpaceIndex));
                if (nextSpaceIndex != _selectedSpaceIndex)
                    SelectSpace(nextSpaceIndex);

                if (_editSpace == EditSpace.Obstacle)
                {
                    int maxObstacleIndex = GetMaxObstacleIndex(map);
                    using (new EditorGUI.DisabledScope(maxObstacleIndex < 0))
                    {
                        int nextObstacleIndex = EditorGUILayout.IntSlider("Obstacle Index", Mathf.Clamp(_selectedObstacleIndex, 0, Mathf.Max(0, maxObstacleIndex)), 0, Mathf.Max(0, maxObstacleIndex));
                        if (nextObstacleIndex != _selectedObstacleIndex)
                            SelectObstacle(nextObstacleIndex);
                    }
                }

                int maxPointIndex = GetMaxPointIndex(map);
                int nextPointIndex = EditorGUILayout.IntSlider("Point Index", Mathf.Clamp(_selectedPointIndex, 0, Mathf.Max(0, maxPointIndex)), 0, Mathf.Max(0, maxPointIndex));
                if (nextPointIndex != _selectedPointIndex)
                    _selectedPointIndex = nextPointIndex;

                if (_editSpace == EditSpace.Transition)
                    DrawSelectedTransitionHeightFields(map);
            }

            EditorGUILayout.Space();
        }

        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Add Flat Region"))
            AddFlatRegion(map);

        if (GUILayout.Button("Add Stair Transition"))
            AddTransition(map, MapNavTransitionType.Stair);

        if (GUILayout.Button("Add Obstacle"))
            AddObstacle(map);

        if (GUILayout.Button("Validate Navigation"))
            LogValidation(map);
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
    }

    private void DrawRegions(MapNavigationAuthoring map)
    {
        for (int i = 0; i < map.Regions.Count; i++)
        {
            MapNavRegion region = map.Regions[i];
            if (region == null || region.Points.Count < 2)
                continue;

            Color color = _editSpace == EditSpace.Region && i == _selectedSpaceIndex ? Color.yellow : new Color(1f, 0.55f, 0.2f);
            Handles.color = color;

            for (int p = 0; p < region.Points.Count; p++)
            {
                Vector3 a = map.ToWorld(region, region.Points[p]);
                Vector3 b = map.ToWorld(region, region.Points[(p + 1) % region.Points.Count]);
                Handles.DrawLine(a, b, 3f);
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

            Handles.color = _editSpace == EditSpace.Transition && i == _selectedSpaceIndex ? Color.yellow : GetTransitionColor(transition);

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

            for (int obstacleIndex = 0; obstacleIndex < region.Obstacles.Count; obstacleIndex++)
            {
                MapNavObstacle obstacle = region.Obstacles[obstacleIndex];
                if (obstacle == null || obstacle.Points.Count < 2)
                    continue;

                Handles.color = _editSpace == EditSpace.Obstacle
                    && regionIndex == _selectedSpaceIndex
                    && obstacleIndex == _selectedObstacleIndex
                        ? Color.yellow
                        : new Color(0.8f, 0.25f, 1f);

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

        if (_selectedPointIndex < 0 || _selectedPointIndex >= region.Points.Count)
            _selectedPointIndex = region.Points.Count > 0 ? 0 : -1;

        DrawPointSelectors(map, region);
        DrawRegionEdgeHandles(map, region);

        if (_selectedPointIndex < 0 || _selectedPointIndex >= region.Points.Count)
            return;

        Vector3 worldPoint = map.ToWorld(region, region.Points[_selectedPointIndex]);

        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.PositionHandle(worldPoint, Quaternion.identity);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(map, "Edit Map Navigation Region Point");
            Vector3 local = map.transform.InverseTransformPoint(moved);
            region.Points[_selectedPointIndex] = new Vector2(local.x, local.z);
            map.RebuildRuntimeData();
            EditorUtility.SetDirty(map);
        }
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

        if (_selectedPointIndex < 0 || _selectedPointIndex >= transition.Points.Count)
            return;

        Vector3 worldPoint = map.ToWorld(transition, transition.Points[_selectedPointIndex]);

        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.PositionHandle(worldPoint, Quaternion.identity);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(map, "Edit Map Navigation Transition Point");
            Vector3 local = map.transform.InverseTransformPoint(moved);
            transition.Points[_selectedPointIndex] = new Vector2(local.x, local.z);
            map.RebuildRuntimeData();
            EditorUtility.SetDirty(map);
        }
    }

    private void EditSelectedObstacle(MapNavigationAuthoring map)
    {
        if (!TryGetSelectedObstacle(map, out MapNavRegion region, out MapNavObstacle obstacle))
            return;

        if (_selectedPointIndex < 0 || _selectedPointIndex >= obstacle.Points.Count)
            _selectedPointIndex = obstacle.Points.Count > 0 ? 0 : -1;

        DrawObstaclePointSelectors(map, region, obstacle);
        DrawObstacleEdgeHandles(map, region, obstacle);

        if (_selectedPointIndex < 0 || _selectedPointIndex >= obstacle.Points.Count)
            return;

        Vector3 worldPoint = map.ToWorld(region, obstacle.Points[_selectedPointIndex]);

        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.PositionHandle(worldPoint, Quaternion.identity);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(map, "Edit Map Navigation Obstacle Point");
            Vector3 local = map.transform.InverseTransformPoint(moved);
            obstacle.Points[_selectedPointIndex] = new Vector2(local.x, local.z);
            map.RebuildRuntimeData();
            EditorUtility.SetDirty(map);
        }
    }

    private void DrawTransitionHeightHandles(MapNavigationAuthoring map, MapNavTransition transition)
    {
        if (transition.Points.Count == 0)
            return;

        MapNavigationQuery.GetTransitionEndpointCenters(transition, out Vector2 fromCenter, out Vector2 toCenter);

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
        _selectedObstacleIndex = 0;
        _selectedPointIndex = 0;
        SceneView.RepaintAll();
    }

    private void SelectObstacle(int obstacleIndex)
    {
        if (_selectedObstacleIndex == obstacleIndex)
            return;

        _selectedObstacleIndex = obstacleIndex;
        _selectedPointIndex = 0;
        SceneView.RepaintAll();
    }

    private void DrawPointSelectors(MapNavigationAuthoring map, MapNavRegion region)
    {
        for (int i = 0; i < region.Points.Count; i++)
        {
            Vector3 worldPoint = map.ToWorld(region, region.Points[i]);
            float size = HandleUtility.GetHandleSize(worldPoint) * 0.08f;
            Color previous = Handles.color;
            Handles.color = i == _selectedPointIndex ? Color.green : Color.white;

            if (Handles.Button(worldPoint, Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap))
            {
                _selectedPointIndex = i;
                SceneView.RepaintAll();
            }

            Handles.color = previous;
        }
    }

    private void DrawTransitionPointSelectors(MapNavigationAuthoring map, MapNavTransition transition)
    {
        for (int i = 0; i < transition.Points.Count; i++)
        {
            Vector3 worldPoint = map.ToWorld(transition, transition.Points[i]);
            float size = HandleUtility.GetHandleSize(worldPoint) * 0.08f;
            Color previous = Handles.color;
            Handles.color = i == _selectedPointIndex ? Color.green : Color.cyan;

            if (Handles.Button(worldPoint, Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap))
            {
                _selectedPointIndex = i;
                SceneView.RepaintAll();
            }

            Handles.color = previous;
        }
    }

    private void DrawObstaclePointSelectors(MapNavigationAuthoring map, MapNavRegion region, MapNavObstacle obstacle)
    {
        for (int i = 0; i < obstacle.Points.Count; i++)
        {
            Vector3 worldPoint = map.ToWorld(region, obstacle.Points[i]);
            float size = HandleUtility.GetHandleSize(worldPoint) * 0.08f;
            Color previous = Handles.color;
            Handles.color = i == _selectedPointIndex ? Color.green : new Color(0.8f, 0.25f, 1f);

            if (Handles.Button(worldPoint, Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap))
            {
                _selectedPointIndex = i;
                SceneView.RepaintAll();
            }

            Handles.color = previous;
        }
    }

    private static void DrawRegionEdgeHandles(MapNavigationAuthoring map, MapNavRegion region)
    {
        DrawEdgeHandles(
            map,
            region.Points,
            point => map.ToWorld(region, point),
            "Move Map Navigation Region Edge",
            new Color(1f, 0.85f, 0.25f)
        );
    }

    private static void DrawTransitionEdgeHandles(MapNavigationAuthoring map, MapNavTransition transition)
    {
        DrawEdgeHandles(
            map,
            transition.Points,
            point => map.ToWorld(transition, point),
            "Move Map Navigation Transition Edge",
            new Color(0.35f, 0.9f, 1f)
        );
    }

    private static void DrawObstacleEdgeHandles(MapNavigationAuthoring map, MapNavRegion region, MapNavObstacle obstacle)
    {
        DrawEdgeHandles(
            map,
            obstacle.Points,
            point => map.ToWorld(region, point),
            "Move Map Navigation Obstacle Edge",
            new Color(1f, 0.35f, 1f)
        );
    }

    private static void DrawEdgeHandles(
        MapNavigationAuthoring map,
        List<Vector2> points,
        System.Func<Vector2, Vector3> toWorld,
        string undoName,
        Color color)
    {
        if (points == null || points.Count < 2)
            return;

        Color previous = Handles.color;
        Handles.color = color;

        for (int i = 0, previousIndex = points.Count - 1; i < points.Count; previousIndex = i++)
        {
            Vector2 localA = points[previousIndex];
            Vector2 localB = points[i];
            Vector3 worldA = toWorld(localA);
            Vector3 worldB = toWorld(localB);
            Vector3 midpoint = (worldA + worldB) * 0.5f;
            float size = HandleUtility.GetHandleSize(midpoint) * 0.075f;

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.FreeMoveHandle(
                midpoint,
                size,
                Vector3.one * 0.05f,
                Handles.RectangleHandleCap
            );

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(map, undoName);
                Vector3 localMidpoint = map.transform.InverseTransformPoint(midpoint);
                Vector3 localMoved = map.transform.InverseTransformPoint(moved);
                Vector2 delta = new(localMoved.x - localMidpoint.x, localMoved.z - localMidpoint.z);

                points[previousIndex] += delta;
                points[i] += delta;

                map.RebuildRuntimeData();
                EditorUtility.SetDirty(map);
            }
        }

        Handles.color = previous;
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
            return region != null ? region.Points.Count - 1 : -1;
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

    private static void AddFlatRegion(MapNavigationAuthoring map)
    {
        Undo.RecordObject(map, "Add Map Navigation Region");

        int id = map.GetNextRegionId();
        map.AddRegion(new MapNavRegion
        {
            Id = id,
            NavLayerId = 0,
            Points =
            {
                new Vector2(-1f, -1f),
                new Vector2(-1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, -1f)
            }
        });

        EditorUtility.SetDirty(map);
    }

    private static void AddTransition(MapNavigationAuthoring map, MapNavTransitionType type)
    {
        Undo.RecordObject(map, "Add Map Navigation Transition");

        int id = map.GetNextTransitionId();
        map.AddTransition(new MapNavTransition
        {
            Id = id,
            Type = type,
            FromHeight = 0f,
            ToHeight = type == MapNavTransitionType.Edge || type == MapNavTransitionType.Door ? 0f : 3f,
            CanStopInside = true,
            CanFightInside = false,
            Points =
            {
                new Vector2(-0.5f, -2f),
                new Vector2(-0.5f, 2f),
                new Vector2(0.5f, 2f),
                new Vector2(0.5f, -2f)
            }
        });

        EditorUtility.SetDirty(map);
    }

    private void AddObstacle(MapNavigationAuthoring map)
    {
        Undo.RecordObject(map, "Add Map Navigation Obstacle");

        if (map.Regions.Count == 0)
            return;

        int regionIndex = Mathf.Clamp(_selectedSpaceIndex, 0, map.Regions.Count - 1);
        MapNavRegion region = map.Regions[regionIndex];
        region.Obstacles.Add(new MapNavObstacle
        {
            Points =
            {
                new Vector2(-0.5f, -0.5f),
                new Vector2(-0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, -0.5f)
            }
        });

        _editSpace = EditSpace.Obstacle;
        _selectedSpaceIndex = regionIndex;
        _selectedObstacleIndex = region.Obstacles.Count - 1;
        _selectedPointIndex = 0;
        map.RebuildRuntimeData();
        EditorUtility.SetDirty(map);
        SceneView.RepaintAll();
    }

    private static void LogValidation(MapNavigationAuthoring map)
    {
        List<string> results = MapNavigationValidator.Validate(map);
        if (results.Count == 0)
        {
            Debug.Log("Map navigation validation passed.", map);
            return;
        }

        for (int i = 0; i < results.Count; i++)
            Debug.LogWarning(results[i], map);
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
        int navLayerId = property.FindPropertyRelative("NavLayerId")?.intValue ?? -1;
        float height = property.FindPropertyRelative("Height")?.floatValue ?? 0f;
        int pointCount = property.FindPropertyRelative("Points")?.arraySize ?? 0;
        int obstacleCount = property.FindPropertyRelative("Obstacles")?.arraySize ?? 0;
        return $"Region {id} | Layer {navLayerId} | H {height:0.##} | P {pointCount} | Obs {obstacleCount}";
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
