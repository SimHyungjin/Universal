using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(MapNavAgent))]
public sealed class MapNavAgentDebug : MonoBehaviour
{
    [SerializeField] private Vector3 labelOffset = new(0f, 2f, 0f);

    private MapNavAgent _agent;
    private MapNavClickMover _clickMover;

    private void Awake()
    {
        _agent = GetComponent<MapNavAgent>();
        _clickMover = GetComponent<MapNavClickMover>();
    }

    private void OnGUI()
    {
        if (_agent == null || Camera.main == null)
            return;

        Vector3 screen = Camera.main.WorldToScreenPoint(transform.position + labelOffset);
        if (screen.z <= 0f)
            return;

        Rect rect = new(screen.x - 110f, Screen.height - screen.y - 24f, 220f, 48f);
        string clickStatus = _clickMover != null ? _clickMover.LastClickStatus : "No Click Mover";
        GUI.Label(rect, $"{_agent.CurrentSpaceName}\n{clickStatus}");
    }
}
