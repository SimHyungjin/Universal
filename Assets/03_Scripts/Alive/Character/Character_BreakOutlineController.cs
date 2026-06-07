using UnityEngine;

[RequireComponent(typeof(Character_Vitals))]
[DisallowMultipleComponent]
public sealed class Character_BreakOutlineController : MonoBehaviour
{
    private Character_Vitals _vitals;
    private Renderer[] _renderers;
    private uint[] _originalRenderingLayerMasks;
    private uint _brokenRenderingLayerMask = 8u;
    private bool _brokenOutlineApplied;

    private void Awake()
    {
        _vitals = GetComponent<Character_Vitals>();
        CacheRenderers();

        if (_vitals != null)
            _vitals.OnBroken += ApplyBrokenOutline;
    }

    private void OnDestroy()
    {
        if (_vitals != null)
            _vitals.OnBroken -= ApplyBrokenOutline;

        RestoreOriginalOutlines();
    }

    private void LateUpdate()
    {
        if (_brokenOutlineApplied && (_vitals == null || !_vitals.IsBroken))
            RestoreOriginalOutlines();
    }

    public void SetBrokenRenderingLayerMask(uint renderingLayerMask)
    {
        _brokenRenderingLayerMask = renderingLayerMask;

        if (!_brokenOutlineApplied)
            return;

        if (_brokenRenderingLayerMask == 0u)
            RestoreOriginalOutlines();
        else
            ApplyRenderingLayerMask(_brokenRenderingLayerMask);
    }

    private void ApplyBrokenOutline()
    {
        if (_brokenRenderingLayerMask == 0u)
            return;

        CacheRenderers();
        ApplyRenderingLayerMask(_brokenRenderingLayerMask);
        _brokenOutlineApplied = true;
    }

    private void ApplyRenderingLayerMask(uint renderingLayerMask)
    {
        if (_renderers == null)
            return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer targetRenderer = _renderers[i];
            if (targetRenderer != null)
                targetRenderer.renderingLayerMask = renderingLayerMask;
        }
    }

    private void RestoreOriginalOutlines()
    {
        if (!_brokenOutlineApplied || _renderers == null || _originalRenderingLayerMasks == null)
            return;

        int count = Mathf.Min(_renderers.Length, _originalRenderingLayerMasks.Length);
        for (int i = 0; i < count; i++)
        {
            Renderer targetRenderer = _renderers[i];
            if (targetRenderer != null)
                targetRenderer.renderingLayerMask = _originalRenderingLayerMasks[i];
        }

        _brokenOutlineApplied = false;
    }

    private void CacheRenderers()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        _originalRenderingLayerMasks = new uint[_renderers.Length];

        for (int i = 0; i < _renderers.Length; i++)
            _originalRenderingLayerMasks[i] = _renderers[i] != null ? _renderers[i].renderingLayerMask : 0u;
    }
}
