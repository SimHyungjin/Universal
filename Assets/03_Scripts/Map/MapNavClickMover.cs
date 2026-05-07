using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(MapNavAgent))]
public sealed class MapNavClickMover : MonoBehaviour
{
    private enum ClickButton
    {
        Left,
        Right,
        Middle
    }

    private enum ClickTargetMode
    {
        NavigationPlane,
        PhysicsRaycast,
        PhysicsRaycastWithPlaneFallback
    }

    [SerializeField] private MapNavigationAuthoring navigation;
    [SerializeField] private ClickButton clickButton = ClickButton.Right;
    [SerializeField] private ClickTargetMode clickTargetMode = ClickTargetMode.NavigationPlane;
    [SerializeField] private bool logDebug;
    [SerializeField] private LayerMask groundMask = ~0;

    private MapNavAgent _agent;
    private bool _wasClickPressed;
    private string _lastClickStatus = "No Click";

    public string LastClickStatus => _lastClickStatus;

    private void Awake()
    {
        _agent = GetComponent<MapNavAgent>();

        if (navigation == null)
            navigation = FindFirstObjectByType<MapNavigationAuthoring>();
    }

    private void Update()
    {
        CaptureClickTarget();
    }

    private void CaptureClickTarget()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            Log("Mouse.current is null.");
            return;
        }

        bool isPressed = IsConfiguredButtonPressed(mouse);
        bool wasPressedThisFrame = isPressed && !_wasClickPressed;
        _wasClickPressed = isPressed;

        if (!wasPressedThisFrame)
            return;

        Log($"{clickButton} click received. MousePosition={mouse.position.ReadValue()}");

        Camera cam = Camera.main;
        if (cam == null)
        {
            Log("Camera.main is null.");
            return;
        }

        Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        if (clickTargetMode == ClickTargetMode.NavigationPlane)
        {
            TrySetNavigationPlaneTarget(ray);
            return;
        }

        if (Physics.Raycast(ray, out RaycastHit hit, 500f, groundMask, QueryTriggerInteraction.Ignore))
        {
            _agent.SetTarget(hit.point);
            _lastClickStatus = $"Hit {hit.collider.name}";
            Log($"Raycast hit '{hit.collider.name}' at {hit.point}. Target set.");
            return;
        }

        Log("Physics raycast missed.");

        if (clickTargetMode == ClickTargetMode.PhysicsRaycastWithPlaneFallback)
        {
            TrySetNavigationPlaneTarget(ray);
            return;
        }

        _lastClickStatus = "Click missed";
        Log("Click missed. No target set.");
    }

    private void TrySetNavigationPlaneTarget(Ray ray)
    {
        if (TryRaycastNavigationPlane(ray, out Vector3 fallbackPoint))
        {
            _agent.SetTarget(fallbackPoint);
            _lastClickStatus = "Hit Navigation Plane";
            Log($"Navigation plane hit at {fallbackPoint}. Target set.");
            return;
        }

        _lastClickStatus = "Click missed";
        Log("Navigation plane click missed. No target set.");
    }

    private bool TryRaycastNavigationPlane(Ray ray, out Vector3 point)
    {
        float planeHeight = navigation != null
            ? navigation.transform.position.y
            : transform.position.y;

        Plane plane = new(Vector3.up, new Vector3(0f, planeHeight, 0f));
        if (plane.Raycast(ray, out float enter))
        {
            point = ray.GetPoint(enter);
            return true;
        }

        point = default;
        return false;
    }

    private bool IsConfiguredButtonPressed(Mouse mouse)
    {
        return clickButton switch
        {
            ClickButton.Left => mouse.leftButton.isPressed,
            ClickButton.Middle => mouse.middleButton.isPressed,
            _ => mouse.rightButton.isPressed
        };
    }

    private void Log(string message)
    {
        if (!logDebug)
            return;

        Debug.Log($"[MapNavClickMover] {message}", this);
    }
}
