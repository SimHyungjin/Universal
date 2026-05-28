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

    // 월드(ECS·적·물리) 시간 배율 — Time.timeScale과 연동.
    public float WorldTimeScale { get; private set; } = 1f;

    // 플레이어 전용 시간 배율 — Time.timeScale과 무관하게 독립 동작.
    public float PlayerTimeScale { get; private set; } = 1f;

    #endregion

    #region Events

    public event Action<float> OnUpdate;
    public event Action<float> OnGameUpdate;
    public event Action<float> OnLateUpdate;

    #endregion

    #region Update

    public void Update(float deltaTime) => OnUpdate?.Invoke(deltaTime);

    public void GameUpdate(float deltaTime) => OnGameUpdate?.Invoke(deltaTime * PlayerTimeScale);

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

    /// <summary>월드·플레이어 시간 배율을 동일하게 설정합니다. (히트스톱·일시정지 등 둘 다 멈추는 경우)</summary>
    public void SetGameSpeed(float gameSpeed) => SetTimeScales(gameSpeed, gameSpeed);

    /// <summary>월드와 플레이어 시간 배율을 개별 설정합니다.</summary>
    public void SetTimeScales(float worldScale, float playerScale)
    {
        WorldTimeScale = worldScale;
        PlayerTimeScale = playerScale;
        Time.timeScale = worldScale;
        _listSequence.RemoveAll(seq => !seq.isAlive);
    }

    /// <summary>월드(ECS·적·물리)만 즉시 정지시키고 플레이어는 정상 동작시킵니다.</summary>
    public void FreezeWorldOnly() => SetTimeScales(0f, 1f);

    public async UniTaskVoid DoFadeGameSpeed(float targetSpeed, float duration)
    {
        CancelSlowMotion();
        var cts = new CancellationTokenSource();
        _slowMotionCTS = cts;
        var token = cts.Token;

        float currentSpeed = WorldTimeScale;
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
            // 다른 페이드가 이미 _slowMotionCTS를 덮어썼다면 건드리지 않는다.
            if (_slowMotionCTS == cts)
                _slowMotionCTS = null;
            cts.Dispose();
        }
    }

    /// <summary>
    /// 월드(ECS·적·물리) 시간 배율만 targetScale로 서서히 변화시킵니다.
    /// 플레이어 시간 배율은 건드리지 않으므로 월드가 느려지거나 멈춰도 플레이어는 그대로 움직입니다.
    /// </summary>
    public async UniTaskVoid DoFadeWorldTimeScale(float targetScale, float duration)
    {
        CancelSlowMotion();
        var cts = new CancellationTokenSource();
        _slowMotionCTS = cts;
        var token = cts.Token;

        float startScale = WorldTimeScale;
        float elapsed = 0f;

        try
        {
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetTimeScales(Mathf.Lerp(startScale, targetScale, elapsed / duration), PlayerTimeScale);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
            SetTimeScales(targetScale, PlayerTimeScale);
        }
        catch (OperationCanceledException) { }
        finally
        {
            // 다른 페이드가 이미 _slowMotionCTS를 덮어썼다면 건드리지 않는다.
            if (_slowMotionCTS == cts)
                _slowMotionCTS = null;
            cts.Dispose();
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
        WorldTimeScale = 1f;
        PlayerTimeScale = 1f;
        Time.timeScale = 1f;
    }

    #endregion
}
