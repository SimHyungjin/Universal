using UnityEngine;

// Character_AttackController의 에디터 기즈모 드로잉 분리분(동작 로직과 무관한 시각화 전용).
public partial class Character_AttackController
{
    private void OnDrawGizmosSelected()
    {
        SO_Attack_Data gizmoData = Application.isPlaying && _currentData != null && IsAttacking
            ? _currentData
            : drawAttackGizmosAlways
                ? GetGizmoData()
                : null;
        if (gizmoData == null) return;

        if (!AttackTimelineUtility.TryGetPrimaryHitVolume(gizmoData, out AttackHitboxData hitbox, out AttackShapeData shape))
            return;

        Color solid = attackGizmoColor;
        solid.a = Mathf.Clamp01(solid.a);
        Gizmos.color = solid;
        DrawAttackShapeGizmo(transform.position, transform.forward, hitbox, shape, true);
        Color wire = solid;
        wire.a = 0.9f;
        Gizmos.color = wire;
        DrawAttackShapeGizmo(transform.position, transform.forward, hitbox, shape, false);
    }

    private SO_Attack_Data GetGizmoData()
    {
        if (_attacks == null || _attacks.Length == 0)
            return null;

        return _attacks[Mathf.Clamp(gizmoAttackIndex, 0, _attacks.Length - 1)];
    }

    private static void DrawAttackShapeGizmo(Vector3 origin, Vector3 forward, AttackHitboxData hitbox, AttackShapeData shape, bool solid)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        switch (shape.type)
        {
            case AttackShape.Cone:
                DrawConeGizmo(origin, forward, hitbox, shape, solid);
                break;
            case AttackShape.Box:
                DrawBoxGizmo(origin, forward, hitbox, shape, solid);
                break;
            default:
                Vector3 center = origin + forward * hitbox.offset + Vector3.up * hitbox.yOffset;
                DrawCylinderGizmo(center, Mathf.Max(0f, shape.radius), shape.verticalTolerance, solid);
                break;
        }
    }

    private static void DrawConeGizmo(Vector3 origin, Vector3 forward, AttackHitboxData hitbox, AttackShapeData shape, bool solid)
    {
        Vector3 apex = origin + forward * hitbox.offset + Vector3.up * hitbox.yOffset;
        float length = Mathf.Max(shape.radius, shape.length);
        float halfAngle = Mathf.Clamp(shape.angle, 1f, 360f) * 0.5f;
        float verticalTolerance = Mathf.Max(0f, shape.verticalTolerance);
        int segments = 24;

        if (solid)
        {
#if UNITY_EDITOR
            Vector3 solidUp = Vector3.up * verticalTolerance;
            Vector3 fromDir = Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward;
            UnityEditor.Handles.color = Gizmos.color;
            UnityEditor.Handles.DrawSolidArc(apex + solidUp, Vector3.up, fromDir, shape.angle, length);
            UnityEditor.Handles.DrawSolidArc(apex - solidUp, Vector3.up, fromDir, shape.angle, length);
#endif
            return;
        }

        Vector3 up = Vector3.up * verticalTolerance;
        DrawConeSlice(apex + up, forward, length, halfAngle, segments);
        DrawConeSlice(apex - up, forward, length, halfAngle, segments);

        Vector3 leftTop = apex + up + Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward * length;
        Vector3 rightTop = apex + up + Quaternion.AngleAxis(halfAngle, Vector3.up) * forward * length;
        Vector3 leftBottom = leftTop - up * 2f;
        Vector3 rightBottom = rightTop - up * 2f;
        Gizmos.DrawLine(apex + up, apex - up);
        Gizmos.DrawLine(leftTop, leftBottom);
        Gizmos.DrawLine(rightTop, rightBottom);
    }

    private static void DrawConeSlice(Vector3 apex, Vector3 forward, float length, float halfAngle, int segments)
    {
        Vector3 previous = apex + Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward * length;
        Gizmos.DrawLine(apex, previous);
        for (int i = 1; i <= segments; i++)
        {
            float t = Mathf.Lerp(-halfAngle, halfAngle, i / (float)segments);
            Vector3 next = apex + Quaternion.AngleAxis(t, Vector3.up) * forward * length;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
        Gizmos.DrawLine(apex, previous);
    }

    private static void DrawBoxGizmo(Vector3 origin, Vector3 forward, AttackHitboxData hitbox, AttackShapeData shape, bool solid)
    {
        float length = shape.length;
        float width  = shape.width;
        float height = Mathf.Max(0.05f, Mathf.Max(0f, shape.verticalTolerance) * 2f);
        Vector3 center = origin + forward * (hitbox.offset + length * 0.5f) + Vector3.up * hitbox.yOffset;
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
        Vector3 size = new Vector3(width, height, length);
        if (solid) Gizmos.DrawCube(Vector3.zero, size);
        else Gizmos.DrawWireCube(Vector3.zero, size);
        Gizmos.matrix = previousMatrix;
    }

    private static void DrawCylinderGizmo(Vector3 center, float radius, float verticalTolerance, bool solid)
    {
        float halfHeight = Mathf.Max(0f, verticalTolerance);
        if (solid)
        {
#if UNITY_EDITOR
            Vector3 solidUp = Vector3.up * halfHeight;
            UnityEditor.Handles.color = Gizmos.color;
            UnityEditor.Handles.DrawSolidDisc(center + solidUp, Vector3.up, radius);
            UnityEditor.Handles.DrawSolidDisc(center,           Vector3.up, radius);
            UnityEditor.Handles.DrawSolidDisc(center - solidUp, Vector3.up, radius);
#endif
            return;
        }

        Vector3 up = Vector3.up * halfHeight;
        DrawCircle(center, radius);
        DrawCircle(center + up, radius);
        DrawCircle(center - up, radius);

        Vector3[] anchors =
        {
            Vector3.forward,
            Vector3.back,
            Vector3.right,
            Vector3.left
        };

        for (int i = 0; i < anchors.Length; i++)
        {
            Vector3 offset = anchors[i] * radius;
            Gizmos.DrawLine(center + up + offset, center - up + offset);
        }
    }

    private static void DrawCircle(Vector3 center, float radius)
    {
        const int segments = 32;
        if (radius <= 0f)
            return;

        Vector3 previous = center + Vector3.forward * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
