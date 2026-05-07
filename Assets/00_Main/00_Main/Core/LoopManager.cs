using Cysharp.Threading.Tasks;
using PrimeTween;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

/// <summary>
/// LoopMonoBehaviour를 상속하면 OnEnable/OnDisable에서 루프 이벤트를 자동으로 구독/해제합니다.
/// </summary>
public abstract class LoopMonoBehaviour : MonoBehaviour
{
    protected virtual void OnEnable()
    {
        if (Main.Loop != null)
        {
            Main.Loop.OnUpdate += OnUpdate;
            Main.Loop.OnGameUpdate += OnGameUpdate;
            Main.Loop.OnLateUpdate += OnLateUpdate;
        }
    }

    protected virtual void OnDisable()
    {
        if (Main.Loop != null)
        {
            Main.Loop.OnUpdate -= OnUpdate;
            Main.Loop.OnGameUpdate -= OnGameUpdate;
            Main.Loop.OnLateUpdate -= OnLateUpdate;
        }
    }

    protected virtual void OnUpdate(float dt) { }
    protected virtual void OnGameUpdate(float gdt) { }
    protected virtual void OnLateUpdate(float dt) { }
}

public class LoopManager : PrimaryManager
{
    #region Fields

    private List<Sequence> _listSequence = new();
    private CancellationTokenSource _slowMotionCTS;

    #endregion

    #region Properties

    public float GameSpeed { get; private set; } = 1f;

    #endregion

    #region Events

    public event Action<float> OnUpdate;
    public event Action<float> OnGameUpdate;
    public event Action<float> OnLateUpdate;

    #endregion

    #region Update

    public void Update(float deltaTime) => OnUpdate?.Invoke(deltaTime);

    public void GameUpdate(float deltaTime) => OnGameUpdate?.Invoke(deltaTime * GameSpeed);

    public void LateUpdate(float deltaTime) => OnLateUpdate?.Invoke(deltaTime);

    #endregion

    #region Time Control

    /// <summary>
    /// 게임 속도에 맞춰 동작하는 PrimeTween 시퀀스를 생성합니다.
    /// 완료된 시퀀스는 게임 속도 갱신 시 목록에서 제거됩니다.
    /// </summary>
    public Sequence GetGameSequence()
    {
        Sequence seq = Sequence.Create();
        _listSequence.Add(seq);
        return seq;
    }

    public void SetGameSpeed(float gameSpeed)
    {
        GameSpeed = gameSpeed;
        _listSequence.RemoveAll(seq => !seq.isAlive);
        foreach (Sequence seq in _listSequence)
            seq.timeScale = gameSpeed;
    }

    public async UniTaskVoid DoFadeGameSpeed(float targetSpeed, float duration)
    {
        CancelSlowMotion();
        _slowMotionCTS = new CancellationTokenSource();
        var token = _slowMotionCTS.Token;

        float currentSpeed = GameSpeed;
        float elapsed = 0f;

        try
        {
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetGameSpeed(Mathf.Lerp(currentSpeed, targetSpeed, elapsed / duration));
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
            SetGameSpeed(targetSpeed);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _slowMotionCTS?.Dispose();
            _slowMotionCTS = null;
        }
    }

    #endregion

    #region Cleanup

    public void ResetGameEvent()
    {
        OnGameUpdate = null;
        OnLateUpdate = null;
    }

    private void CancelSlowMotion()
    {
        if (_slowMotionCTS == null) return;
        _slowMotionCTS.Cancel();
        _slowMotionCTS.Dispose();
        _slowMotionCTS = null;
    }

    public override void Clear()
    {
        CancelSlowMotion();
        OnUpdate = null;
        OnGameUpdate = null;
        OnLateUpdate = null;
        foreach (var seq in _listSequence)
            seq.Stop();
        _listSequence.Clear();
        GameSpeed = 1f;
    }

    #endregion
}
