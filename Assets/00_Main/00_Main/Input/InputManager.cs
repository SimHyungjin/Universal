using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;

/// <summary>
/// InputSystem_Actions는 Unity Input Actions 에셋에서 자동 생성되는 클래스입니다.
/// 새 프로젝트에서 Input Actions 에셋을 만들고 "Generate C# Class"를 활성화한 뒤
/// 클래스 이름을 InputSystem_Actions로 지정하면 이 매니저와 연동됩니다.
/// </summary>
public class InputManager : CoreManager
{
    #region Fields


    private HashSet<Type>               _curActionTypes = new();
    private readonly Dictionary<Type, InputActions> _typeToAction = new();

    #endregion

    #region Initialization

    protected override async UniTask OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        EnhancedTouchSupport.Enable();
        SubscribeUpdate();
    }

    #endregion

    private void SubscribeUpdate()
    {
        if (Main.Loop == null) return;
        Main.Loop.OnUpdate -= UpdateActiveActions;
        Main.Loop.OnUpdate += UpdateActiveActions;
    }

    private void UpdateActiveActions(float deltaTime)
    {
        foreach (var type in _curActionTypes)
        {
            if (_typeToAction.TryGetValue(type, out var action))
                action.OnUpdate(deltaTime);
        }
    }

    #region Input Control

    private void SetInput(params Type[] targetTypes)
    {
        HashSet<Type> nextTypes = targetTypes
            .Where(IsValidActionType)
            .ToHashSet();

        foreach (var cur in _curActionTypes)
            if (!nextTypes.Contains(cur)) DisconnectAction(cur);

        foreach (var next in nextTypes)
            if (!_curActionTypes.Contains(next)) ConnectAction(next);

        _curActionTypes = nextTypes;
    }

    private void AddInput(params Type[] addTypes)
        => SetInput(_curActionTypes.Concat(addTypes).ToArray());

    private void RemoveInput(params Type[] removeTypes)
        => SetInput(_curActionTypes.Where(t => !removeTypes.Contains(t)).ToArray());

    #endregion

    #region Public API

    public T SetInput<T>(Action<T> onInit) where T : InputActions
    {
        var action = GetOrCreateAction<T>();
        onInit?.Invoke(action);
        SetInput(typeof(T));
        return action;
    }

    public void SetInput<T>()                where T : InputActions => SetInput(typeof(T));
    public void SetInput<T1, T2>()           where T1 : InputActions where T2 : InputActions => SetInput(typeof(T1), typeof(T2));
    public void SetInput<T1, T2, T3>()       where T1 : InputActions where T2 : InputActions where T3 : InputActions => SetInput(typeof(T1), typeof(T2), typeof(T3));

    public void AddInput<T>()    where T : InputActions => AddInput(typeof(T));
    public void RemoveInput<T>() where T : InputActions => RemoveInput(typeof(T));
    public void RemoveAllInputs()                       => SetInput();

    #endregion

    #region Query

    public bool IsActive<T>()    where T : InputActions => _curActionTypes.Contains(typeof(T));
    public T GetAction<T>()      where T : InputActions => _typeToAction.TryGetValue(typeof(T), out var a) ? a as T : null;
    public T GetOrCreateAction<T>() where T : InputActions => GetOrCreateAction(typeof(T)) as T;

    #endregion

    #region Internal

    private bool IsValidActionType(Type type)
        => type != null && !type.IsAbstract && typeof(InputActions).IsAssignableFrom(type);

    private void ConnectAction(Type type)
    {
        var action = GetOrCreateAction(type);
        action?.Connect();
    }

    private void DisconnectAction(Type type)
    {
        if (_typeToAction.TryGetValue(type, out var action)) action.Disconnect();
    }

    private InputActions GetOrCreateAction(Type type)
    {
        if (_typeToAction.TryGetValue(type, out var action)) return action;
        return CreateAndCacheAction(type);
    }

    private InputActions CreateAndCacheAction(Type type)
    {
        try
        {
            var action = (InputActions)Activator.CreateInstance(type);
            action.Init(this);
            _typeToAction[type] = action;
            return action;
        }
        catch (Exception e)
        {
            Debug.LogError($"[InputManager] Create Fail: {type.Name}\n{e}");
            return null;
        }
    }

    #endregion

    #region Utility

    private PointerEventData _pointerData;
    private readonly List<RaycastResult> _raycastResults = new(16);

    public bool IsPointerOverUI(Vector2 screenPos)
    {
        if (EventSystem.current == null) return false;
        _pointerData ??= new PointerEventData(EventSystem.current);
        _pointerData.position = screenPos;
        _raycastResults.Clear();
        EventSystem.current.RaycastAll(_pointerData, _raycastResults);
        return _raycastResults.Count > 0;
    }

    public Vector3 ScreenToWorld(Vector2 screenPos)
    {
        var cam = Camera.main;
        if (cam == null) return Vector3.zero;
        float depth = cam.orthographic ? -cam.transform.position.z : 0f;
        return cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));
    }

    #endregion

    #region Cleanup

    public override void Clear()
    {
        base.Clear();
        foreach (var type in _curActionTypes)
            if (_typeToAction.TryGetValue(type, out var action)) action.Disconnect();
        _curActionTypes.Clear();
        _typeToAction.Clear();
        SubscribeUpdate();
    }

    #endregion
}
