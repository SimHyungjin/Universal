using Cysharp.Threading.Tasks;
using UnityEngine;

public enum CombatCameraMode
{
    Tactical = 0,
    ThirdPerson = 1
}

public class CameraManager : CoreManager
{
    private const float MinPerspectiveNearClip = 0.05f;

    private Transform _target;
    private Camera _camera;
    private float _followSpeed = 12f;
    private CombatCameraMode _mode = CombatCameraMode.Tactical;
    private float _followYaw;

    // Transition
    private float _transitionProgress = 1f;
    private Vector3 _transitionFromPos;
    private Quaternion _transitionFromRot;
    private const float TransitionDuration = 0.35f;

    // Spring-back
    private float _springBackTimer;
    private float _thirdPersonAttackAlignTimer;
    private const float SpringBackDelay = 0.5f;
    private const float SpringBackSpeed = 1.5f;

    // Tactical
    private Vector3 _offset = new(5, 9f, -5f);
    private Quaternion _tacticalRotation = Quaternion.identity;
    private float _tacticalOrthographicSize = -1f;

    // ThirdPerson
    private readonly Vector3 _thirdPersonFocusOffset = new(0f, 1.35f, 0f);
    private float _thirdPersonDistance = 6.5f;
    private float _thirdPersonHeight = 3.8f;
    private float _thirdPersonShoulderOffset = 0.4f;
    private float _thirdPersonFov = 45f;
    private float _thirdPersonPositionSpeed = 18f;
    private float _thirdPersonRotationSpeed = 14f;

    public Camera MainCamera
    {
        get
        {
            Camera taggedMain = Camera.main;
            if (taggedMain != null) _camera = taggedMain;
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
        _followSpeed = Mathf.Max(0f, followSpeed);

        if (offset.HasValue)
        {
            _offset = offset.Value;
        }
        else
        {
            Camera cam = MainCamera;
            if (cam != null && _target != null)
                CaptureTacticalState(cam);
        }

        if (_target != null)
            _followYaw = _target.eulerAngles.y;

        if (snap) UpdateCamera(0f, true);
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

        if (orthographic)
            CaptureTacticalState(cam);
    }

    public void ClearFollowTarget(Transform target)
    {
        if (_target == target) _target = null;
    }

    public void SetMode(CombatCameraMode mode, bool snap = false)
    {
        if (_mode == mode) return;
        _mode = mode;

        Camera cam = MainCamera;
        if (cam != null && !snap)
        {
            _transitionFromPos = cam.transform.position;
            _transitionFromRot = cam.transform.rotation;
            _transitionProgress = 0f;
        }
        else
        {
            _transitionProgress = 1f;
        }

        if (_target != null && mode == CombatCameraMode.ThirdPerson)
            _followYaw = GetCameraYaw(MainCamera, _target.eulerAngles.y);

        if (snap) UpdateCamera(0f, true);
    }

    public void ToggleMode()
    {
        SetMode(_mode == CombatCameraMode.ThirdPerson
            ? CombatCameraMode.Tactical
            : CombatCameraMode.ThirdPerson);
    }

    public void AlignThirdPersonToTargetYaw(float duration)
    {
        if (_mode != CombatCameraMode.ThirdPerson || _target == null) return;

        _thirdPersonAttackAlignTimer = Mathf.Max(_thirdPersonAttackAlignTimer, duration);
        _springBackTimer = Mathf.Max(_springBackTimer, SpringBackDelay);
    }

    private void LateUpdate(float deltaTime)
    {
        UpdateCamera(deltaTime, false);
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

    private void CaptureTacticalState(Camera cam)
    {
        _tacticalRotation = cam.transform.rotation;
        if (_target != null)
            _offset = cam.transform.position - _target.position;
        if (cam.orthographic)
            _tacticalOrthographicSize = cam.orthographicSize;
    }

    private void UpdateCamera(float deltaTime, bool snap)
    {
        Camera cam = MainCamera;
        if (cam == null || _target == null) return;

        bool transitioning = !snap && _transitionProgress < 1f;

        if (_mode == CombatCameraMode.ThirdPerson)
            UpdateThirdPerson(cam, deltaTime, snap || transitioning);
        else
            UpdateTactical(cam, deltaTime, snap || transitioning);

        if (transitioning)
        {
            _transitionProgress = Mathf.Min(1f, _transitionProgress + deltaTime / TransitionDuration);
            float t = Mathf.SmoothStep(0f, 1f, _transitionProgress);
            cam.transform.SetPositionAndRotation(
                Vector3.Lerp(_transitionFromPos, cam.transform.position, t),
                Quaternion.Slerp(_transitionFromRot, cam.transform.rotation, t));
        }
    }

    private void UpdateThirdPerson(Camera cam, float deltaTime, bool snap)
    {
        cam.usePhysicalProperties = false;
        cam.orthographic = false;
        if (cam.nearClipPlane < MinPerspectiveNearClip)
            cam.nearClipPlane = MinPerspectiveNearClip;
        cam.fieldOfView = _thirdPersonFov;
        RefreshProjection(cam);

        if (!snap && deltaTime > 0f)
        {
            bool attackAligning = _thirdPersonAttackAlignTimer > 0f;
            if (attackAligning)
                _thirdPersonAttackAlignTimer = Mathf.Max(0f, _thirdPersonAttackAlignTimer - deltaTime);

            bool isMoving = InputProvider.Move.Direction.sqrMagnitude > 0.01f;
            if (isMoving && !attackAligning)
                _springBackTimer = 0f;
            else
                _springBackTimer += deltaTime;

            if (_springBackTimer >= SpringBackDelay)
                _followYaw = Mathf.LerpAngle(_followYaw, _target.eulerAngles.y, DampFactor(SpringBackSpeed, deltaTime));
        }

        Quaternion yawRotation = Quaternion.Euler(0f, _followYaw, 0f);
        Vector3 focus = _target.position + _thirdPersonFocusOffset;
        Vector3 targetPosition = focus
            - yawRotation * Vector3.forward * _thirdPersonDistance
            + Vector3.up * _thirdPersonHeight
            + yawRotation * Vector3.right * _thirdPersonShoulderOffset;
        Quaternion targetRotation = Quaternion.LookRotation(focus - targetPosition, Vector3.up);

        cam.transform.SetPositionAndRotation(
            snap ? targetPosition : Vector3.Lerp(cam.transform.position, targetPosition, DampFactor(_thirdPersonPositionSpeed, deltaTime)),
            snap ? targetRotation : Quaternion.Slerp(cam.transform.rotation, targetRotation, DampFactor(_thirdPersonRotationSpeed, deltaTime)));
    }

    private void UpdateTactical(Camera cam, float deltaTime, bool snap)
    {
        cam.orthographic = true;
        if (_tacticalOrthographicSize > 0f)
            cam.orthographicSize = _tacticalOrthographicSize;
        RefreshProjection(cam);

        Vector3 targetPosition = _target.position + _offset;

        cam.transform.position = snap || _followSpeed <= 0f
            ? targetPosition
            : Vector3.Lerp(cam.transform.position, targetPosition, 1f - Mathf.Exp(-_followSpeed * deltaTime));
        cam.transform.rotation = _tacticalRotation;
    }

    private static float DampFactor(float sharpness, float deltaTime)
    {
        if (sharpness <= 0f) return 1f;
        return 1f - Mathf.Exp(-sharpness * deltaTime);
    }

    private static void RefreshProjection(Camera cam)
    {
        cam.ResetProjectionMatrix();
        cam.ResetCullingMatrix();
    }

    private static float GetCameraYaw(Camera cam, float fallbackYaw)
    {
        if (cam == null) return fallbackYaw;

        Vector3 forward = cam.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            return fallbackYaw;

        return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    }
}
