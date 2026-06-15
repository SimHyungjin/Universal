using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class Character_AttackTelegraph : MonoBehaviour
{
    private const float GroundLift = 0.035f;
    private const float EdgeWidth = 0.08f;
    private const float Opacity = 0.65f;
    private const string TelegraphShaderName = "Universal/Attack Telegraph";

    private static readonly int ColorId = Shader.PropertyToID("_TelegraphColor");
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int ShapeTypeId = Shader.PropertyToID("_ShapeType");
    private static readonly int ShapeParamsId = Shader.PropertyToID("_ShapeParams");
    private static readonly int HitboxOffsetId = Shader.PropertyToID("_HitboxOffset");
    private static readonly int TelegraphSizeId = Shader.PropertyToID("_TelegraphSize");
    private static readonly int FillRangeId = Shader.PropertyToID("_FillRange");
    private static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

    private static Mesh _quadMesh;
    private static Material _sharedMaterial;
    private static Shader _telegraphShader;
    private static bool _shaderResolved;

    private GameObject _planeGo;
    private MeshRenderer _renderer;
    private MaterialPropertyBlock _propertyBlock;

    private Color _color = Color.red;
    private float _halfSize = 2f;
    private float _progress;
    private int _shapeType;
    private Vector4 _shapeParams;
    private float _hitboxOffset;
    private float _fillRange = 1f;

    public void Show(SO_Attack_Data attack, Color color, float leadTime)
    {
        if (!EnsureRenderer() || attack == null)
            return;

        _color = color;
        _progress = 0f;

        ResolveEffectiveShape(attack, out AttackHitboxData hitbox, out AttackShapeData shape);
        ConfigureShape(hitbox, shape);
        UpdateTransform(transform.position, transform.forward);
        ApplyProperties();
        _planeGo.SetActive(true);
    }

    public void Tick(float progress)
    {
        if (_planeGo == null || !_planeGo.activeSelf)
            return;

        _progress = Mathf.Clamp01(progress);
        ApplyProperties();
    }

    public void Hide()
    {
        if (_planeGo != null)
            _planeGo.SetActive(false);
    }

    private bool EnsureRenderer()
    {
        if (_renderer != null)
            return true;

        if (!_shaderResolved)
        {
            _telegraphShader = Shader.Find(TelegraphShaderName);
            _shaderResolved = true;
        }

        if (_telegraphShader == null)
        {
            Debug.LogWarning($"[Telegraph] Shader '{TelegraphShaderName}' was not found. Attack telegraph will not be rendered.");
            return false;
        }

        _sharedMaterial ??= new Material(_telegraphShader)
        {
            name = "AttackTelegraphMaterial",
            hideFlags = HideFlags.HideAndDontSave
        };

        _planeGo = new GameObject("AttackTelegraphPlane");
        _planeGo.layer = gameObject.layer;

        MeshFilter filter = _planeGo.AddComponent<MeshFilter>();
        filter.sharedMesh = GetQuadMesh();

        _renderer = _planeGo.AddComponent<MeshRenderer>();
        _renderer.sharedMaterial = _sharedMaterial;
        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.allowOcclusionWhenDynamic = false;

        _propertyBlock = new MaterialPropertyBlock();
        _planeGo.SetActive(false);
        return true;
    }

    private static Mesh GetQuadMesh()
    {
        if (_quadMesh != null)
            return _quadMesh;

        _quadMesh = new Mesh
        {
            name = "AttackTelegraphQuad",
            hideFlags = HideFlags.HideAndDontSave,
            vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3( 0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f,  0.5f),
                new Vector3( 0.5f, 0f,  0.5f)
            },
            uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            },
            triangles = new[] { 0, 2, 1, 2, 3, 1 }
        };
        _quadMesh.RecalculateNormals();
        _quadMesh.bounds = new Bounds(Vector3.zero, new Vector3(1f, 0.1f, 1f));
        return _quadMesh;
    }

    private void ConfigureShape(AttackHitboxData hitbox, AttackShapeData shape)
    {
        float reach = AttackShapeUtility.GetPlanarReach(shape);
        _halfSize = Mathf.Max(0.5f, hitbox.offset + reach + 0.5f);
        _hitboxOffset = hitbox.offset;
        _shapeType = (int)shape.type;
        _shapeParams = new Vector4(
            Mathf.Max(0f, shape.radius),
            Mathf.Clamp(shape.angle, 1f, 360f) * Mathf.Deg2Rad,
            Mathf.Max(0f, shape.length),
            Mathf.Max(0f, shape.width));
        _fillRange = Mathf.Max(0.001f, GetFillRange(hitbox, shape));
    }

    private static float GetFillRange(AttackHitboxData hitbox, AttackShapeData shape)
    {
        return shape.type switch
        {
            AttackShape.Cone => hitbox.offset + Mathf.Max(shape.radius, shape.length),
            AttackShape.Box => Mathf.Sqrt(shape.width * shape.width * 0.25f + Mathf.Pow(hitbox.offset + shape.length, 2f)),
            _ => hitbox.offset + Mathf.Max(0f, shape.radius)
        };
    }

    private void ApplyProperties()
    {
        if (_renderer == null)
            return;

        _renderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(ColorId, _color);
        _propertyBlock.SetFloat(ProgressId, _progress);
        _propertyBlock.SetFloat(ShapeTypeId, _shapeType);
        _propertyBlock.SetVector(ShapeParamsId, _shapeParams);
        _propertyBlock.SetFloat(HitboxOffsetId, _hitboxOffset);
        _propertyBlock.SetFloat(TelegraphSizeId, _halfSize * 2f);
        _propertyBlock.SetFloat(FillRangeId, _fillRange);
        _propertyBlock.SetFloat(EdgeWidthId, EdgeWidth);
        _propertyBlock.SetFloat(OpacityId, Opacity);
        _renderer.SetPropertyBlock(_propertyBlock);
    }

    private void UpdateTransform(Vector3 attackerPos, Vector3 forward)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        float size = _halfSize * 2f;
        _planeGo.transform.SetPositionAndRotation(attackerPos + Vector3.up * GroundLift, Quaternion.LookRotation(forward, Vector3.up));
        _planeGo.transform.localScale = new Vector3(size, 1f, size);
    }

    private static void ResolveEffectiveShape(SO_Attack_Data attack, out AttackHitboxData hitbox, out AttackShapeData shape)
    {
        if (!AttackTimelineUtility.TryGetPrimaryHitVolume(attack, out hitbox, out shape))
            return;

        // 휩쓸며 때리는 공격(다단 repeat + 전방 이동)은 위험 경로를 레인(Box)으로 보여준다.
        // 단발/제자리는 첫타 shape 그대로 — repeat가 "지속 위험 창" 신호라 매직 거리 임계 없이 가른다.
        float movementDistance = AttackTimelineUtility.GetMaxForwardMovementDistance(attack);
        if (movementDistance > 0f
            && AttackTimelineUtility.HasRepeatingMeleeDelivery(attack)
            && !AttackTimelineUtility.HasSelfJump(attack))
        {
            float reach = AttackShapeUtility.GetPlanarReach(shape);
            float width = shape.type == AttackShape.Box
                ? Mathf.Max(0.5f, shape.width)
                : Mathf.Max(1f, shape.radius * 2f);
            shape = new AttackShapeData
            {
                type = AttackShape.Box,
                length = movementDistance + reach,
                width = width,
                radius = shape.radius,
                angle = shape.angle,
                verticalTolerance = shape.verticalTolerance
            };
            return;
        }

        if (!AttackTimelineUtility.TryGetFirstProjectile(attack, out AttackProjectileDelivery projectile))
            return;

        float range = AttackTimelineUtility.GetProjectileRange(projectile);
        if (range <= 0f)
            range = 10f;
        if (projectile.count > 1 && projectile.spreadAngle > 1f)
        {
            shape = new AttackShapeData
            {
                type = AttackShape.Cone,
                radius = range,
                length = range,
                angle = Mathf.Clamp(projectile.spreadAngle, 1f, 360f),
                width = 0f
            };
        }
        else
        {
            shape = new AttackShapeData
            {
                type = AttackShape.Box,
                length = Mathf.Min(range, 16f),
                width = 2f,
                radius = 0f,
                angle = 0f
            };
        }
    }

    private void OnDestroy()
    {
        if (_planeGo != null)
            Destroy(_planeGo);
    }
}
