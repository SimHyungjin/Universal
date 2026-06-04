using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class DashAfterimage : MonoBehaviour, IPoolable
{
    private MaterialPropertyBlock _propertyBlock;
    private DashAfterimagePart[] _parts = System.Array.Empty<DashAfterimagePart>();
    private float _lifetime;
    private float _age;
    private Color _color;
    private bool _active;

    public void Capture(SkinnedMeshRenderer[] sources, Material material, Color color, float lifetime)
    {
        if (sources == null || sources.Length == 0 || material == null)
        {
            App.Despawn(gameObject);
            return;
        }

        EnsurePartCount(sources.Length);

        _lifetime = Mathf.Max(0.01f, lifetime);
        _age = 0f;
        _color = color;
        _active = true;

        for (int i = 0; i < _parts.Length; i++)
        {
            DashAfterimagePart part = _parts[i];
            SkinnedMeshRenderer source = i < sources.Length ? sources[i] : null;
            bool active = source != null && source.enabled && source.gameObject.activeInHierarchy;
            part.Transform.gameObject.SetActive(active);
            if (!active) continue;

            source.BakeMesh(part.Mesh);
            part.Transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            part.Transform.localScale = source.transform.lossyScale;
            part.Renderer.sharedMaterials = BuildMaterialArray(material, source.sharedMaterials.Length);
            part.Renderer.enabled = true;
        }

        ApplyColor(_color);
    }

    public void OnSpawn()
    {
        _propertyBlock ??= new MaterialPropertyBlock();
        _active = false;
        _age = 0f;
    }

    public void OnDespawn()
    {
        _active = false;
        for (int i = 0; i < _parts.Length; i++)
        {
            if (_parts[i].Renderer != null)
                _parts[i].Renderer.enabled = false;
            if (_parts[i].Transform != null)
                _parts[i].Transform.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (!_active) return;

        _age += Time.deltaTime;
        float remaining = 1f - Mathf.Clamp01(_age / _lifetime);
        if (remaining <= 0f)
        {
            App.Despawn(gameObject);
            return;
        }

        Color faded = _color;
        faded.a *= remaining * remaining;
        ApplyColor(faded);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _parts.Length; i++)
        {
            if (_parts[i].Mesh != null)
                Destroy(_parts[i].Mesh);
        }
    }

    private void EnsurePartCount(int count)
    {
        _parts ??= System.Array.Empty<DashAfterimagePart>();
        if (_parts.Length >= count) return;

        int oldLength = _parts.Length;
        System.Array.Resize(ref _parts, count);
        for (int i = oldLength; i < count; i++)
        {
            GameObject child = new($"Mesh {i:00}");
            child.transform.SetParent(transform, false);

            MeshFilter filter = child.AddComponent<MeshFilter>();
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Mesh mesh = new() { name = $"DashAfterimage_{i:00}" };
            filter.sharedMesh = mesh;
            child.SetActive(false);
            _parts[i] = new DashAfterimagePart(child.transform, renderer, mesh);
        }
    }

    private static Material[] BuildMaterialArray(Material material, int sourceMaterialCount)
    {
        int count = Mathf.Max(1, sourceMaterialCount);
        Material[] materials = new Material[count];
        for (int i = 0; i < materials.Length; i++)
            materials[i] = material;
        return materials;
    }

    private void ApplyColor(Color color)
    {
        _propertyBlock ??= new MaterialPropertyBlock();
        _propertyBlock.Clear();
        _propertyBlock.SetColor("_BaseColor", color);
        _propertyBlock.SetColor("_Color", color);

        for (int i = 0; i < _parts.Length; i++)
        {
            MeshRenderer renderer = _parts[i].Renderer;
            if (renderer != null && renderer.enabled)
                renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    private readonly struct DashAfterimagePart
    {
        public readonly Transform Transform;
        public readonly MeshRenderer Renderer;
        public readonly Mesh Mesh;

        public DashAfterimagePart(Transform transform, MeshRenderer renderer, Mesh mesh)
        {
            Transform = transform;
            Renderer = renderer;
            Mesh = mesh;
        }
    }
}
