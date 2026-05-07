using UnityEngine;

/// <summary>
/// 입력 액션 모듈의 기본 클래스.
/// 상속 후 Connect / Disconnect / OnUpdate를 구현하고
/// App.SetInput&lt;T&gt;()으로 활성화합니다.
/// </summary>
public abstract class InputActions
{
    protected InputManager Manager;
    protected Camera MainCamera => Camera.main;

    private bool _isInitialized;

    public void Init(InputManager manager)
    {
        if (_isInitialized) return;
        Manager  = manager;
        _isInitialized = true;
    }

    public abstract void Connect();
    public abstract void Disconnect();
    public abstract void OnUpdate(float deltaTime);
}