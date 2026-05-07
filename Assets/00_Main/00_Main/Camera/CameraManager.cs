using Cysharp.Threading.Tasks;
using UnityEngine;

public class CameraManager : CoreManager
{
    private Transform _target;
    private Camera _camera;
    private Vector3 _offset = new(5, 9f, -5f);
    private float _followSpeed = 12f;
    private bool _snapOnSet = true;

    public Camera MainCamera
    {
        get
        {
            if (_camera == null) _camera = Camera.main;
            return _camera;
        }
    }

    protected override UniTask OnInitializeAsync()
    {
        _camera = Camera.main;
        SubscribeLateUpdate();
        return base.OnInitializeAsync();
    }

    public void SetFollowTarget(
        Transform target,
        Vector3? offset = null,
        float followSpeed = 12f,
        bool snap = true)
    {
        _target = target;
        if (offset.HasValue)
        {
            _offset = offset.Value;
        }
        else
        {
            Camera cam = MainCamera;
            if (cam != null && _target != null)
                _offset = cam.transform.position - _target.position;
        }

        _followSpeed = Mathf.Max(0f, followSpeed);
        _snapOnSet = snap;

        if (_snapOnSet) UpdateFollow(0f, true);
    }

    public void SetView(
        Vector3 position,
        Vector3 eulerAngles,
        bool orthographic = true,
        float orthographicSize = 4f)
    {
        Camera cam = MainCamera;
        if (cam == null) return;

        cam.transform.SetPositionAndRotation(position, Quaternion.Euler(eulerAngles));
        cam.orthographic = orthographic;
        if (orthographic) cam.orthographicSize = orthographicSize;
    }

    public void ClearFollowTarget(Transform target)
    {
        if (_target == target) _target = null;
    }

    private void LateUpdate(float deltaTime)
    {
        UpdateFollow(deltaTime, false);
    }

    private void SubscribeLateUpdate()
    {
        if (Main.Loop == null) return;
        Main.Loop.OnLateUpdate -= LateUpdate;
        Main.Loop.OnLateUpdate += LateUpdate;
    }

    public override void Clear()
    {
        base.Clear();
        if (Main.Loop != null) Main.Loop.OnLateUpdate -= LateUpdate;
        _target = null;
    }

    private void UpdateFollow(float deltaTime, bool snap)
    {
        Camera cam = MainCamera;
        if (cam == null || _target == null) return;

        Vector3 targetPosition = _target.position + _offset;

        cam.transform.position = snap || _followSpeed <= 0f
            ? targetPosition
            : Vector3.Lerp(cam.transform.position, targetPosition, 1f - Mathf.Exp(-_followSpeed * deltaTime));
    }

}
