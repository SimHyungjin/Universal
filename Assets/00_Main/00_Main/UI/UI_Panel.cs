using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

public abstract class UI_Panel : UI
{
    [HideInInspector] public UnityEvent OnOpenEvent  = new();
    [HideInInspector] public UnityEvent OnCloseEvent = new();

    protected bool _isOpened;

    public virtual void Open()
    {
        if (_isOpened) return;
        _isOpened = true;
        OnOpenEvent?.Invoke();
    }

    public virtual void Close()
    {
        if (!_isOpened) return;
        _isOpened = false;
        OnCloseEvent?.Invoke();
    }

    protected virtual void OnDestroy()
    {
        OnOpenEvent.RemoveAllListeners();
        OnCloseEvent.RemoveAllListeners();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;

        var fields = GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var field in fields)
        {
            if (field.GetCustomAttribute<SerializeField>() == null) continue;

            var currentValue = field.GetValue(this);
            if (currentValue != null && !currentValue.Equals(null)) continue;

            if (typeof(Component).IsAssignableFrom(field.FieldType))
            {
                var target = FindComponent(field.FieldType, field.Name);
                if (target != null)
                {
                    field.SetValue(this, target);
                    UnityEditor.EditorUtility.SetDirty(this);
                }
            }
            else if (field.FieldType == typeof(GameObject))
            {
                var target = FindComponent(typeof(Transform), field.Name);
                if (target != null)
                {
                    field.SetValue(this, (target as Transform).gameObject);
                    UnityEditor.EditorUtility.SetDirty(this);
                }
            }
        }
    }

    private Component FindComponent(System.Type type, string name)
    {
        var components = GetComponentsInChildren(type, true);
        foreach (var c in components)
            if (c.name == name) return c;
        return null;
    }
#endif
}
