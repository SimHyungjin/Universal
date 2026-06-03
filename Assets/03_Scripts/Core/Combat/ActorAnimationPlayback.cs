using System.Collections.Generic;
using UnityEngine;

public enum ActorAnimationTimeDomain
{
    World = 0,
    Player = 1,
    Unscaled = 2
}

public readonly struct ActorLocomotionAnimation
{
    public readonly int IdleHash;
    public readonly int RunHash;
    public readonly float StartRunTransition;
    public readonly float StopRunTransition;
    public readonly float RunEnterDelay;
    public readonly float MinRunDuration;

    public ActorLocomotionAnimation(
        int idleHash,
        int runHash,
        float startRunTransition,
        float stopRunTransition,
        float runEnterDelay,
        float minRunDuration)
    {
        IdleHash = idleHash;
        RunHash = runHash;
        StartRunTransition = startRunTransition;
        StopRunTransition = stopRunTransition;
        RunEnterDelay = runEnterDelay;
        MinRunDuration = minRunDuration;
    }

    public bool IsValid => IdleHash != 0 && RunHash != 0;

    public static ActorLocomotionAnimation FromStateNames(
        string idleStateName,
        string runStateName,
        float startRunTransition,
        float stopRunTransition,
        float runEnterDelay,
        float minRunDuration)
    {
        int idleHash = string.IsNullOrWhiteSpace(idleStateName) ? 0 : Animator.StringToHash(idleStateName);
        int runHash = string.IsNullOrWhiteSpace(runStateName) ? 0 : Animator.StringToHash(runStateName);
        return new ActorLocomotionAnimation(
            idleHash,
            runHash,
            startRunTransition,
            stopRunTransition,
            runEnterDelay,
            minRunDuration);
    }
}

public sealed class ActorAnimationPlayback
{
    private Animator _animator;
    private int _currentHash;
    private float _movingTime;
    private float _runTime;
    private readonly Dictionary<int, string> _stateNamesByHash = new();
    private readonly HashSet<int> _loggedNoControllerHashes = new();
    private readonly HashSet<int> _loggedMissingStateHashes = new();

    public bool IsValid => _animator != null;
    public int CurrentHash => _currentHash;

    public void Bind(Animator animator)
    {
        _animator = animator;
        _loggedNoControllerHashes.Clear();
        _loggedMissingStateHashes.Clear();
        if (_animator != null)
            _animator.updateMode = AnimatorUpdateMode.UnscaledTime;
    }

    public void Reset(int currentHash = 0)
    {
        _currentHash = currentHash;
        ResetLocomotionTimers();
    }

    public void SyncSpeed(ActorAnimationTimeDomain timeDomain)
    {
        if (_animator == null) return;
        _animator.speed = ResolveSpeed(timeDomain);
    }

    public void ResetLocomotionTimers()
    {
        _movingTime = 0f;
        _runTime = 0f;
    }

    public void TickLocomotion(bool moving, float deltaTime, in ActorLocomotionAnimation locomotion)
    {
        if (_animator == null || !locomotion.IsValid) return;

        if (moving)
        {
            _movingTime += deltaTime;
            _runTime += deltaTime;

            if (_currentHash != locomotion.RunHash && _movingTime >= locomotion.RunEnterDelay)
            {
                _runTime = 0f;
                Play(locomotion.RunHash, locomotion.StartRunTransition);
            }

            return;
        }

        _movingTime = 0f;

        if (_currentHash == locomotion.RunHash)
        {
            _runTime += deltaTime;
            if (_runTime < locomotion.MinRunDuration) return;
        }

        Play(locomotion.IdleHash, locomotion.StopRunTransition);
    }

    public void Play(string stateName, float transitionDuration)
    {
        if (string.IsNullOrWhiteSpace(stateName)) return;
        RegisterStateName(stateName);
        Play(Animator.StringToHash(stateName), transitionDuration);
    }

    public void Play(int stateHash, float transitionDuration)
    {
        if (_animator == null || stateHash == 0) return;
        if (_currentHash == stateHash) return;
        if (!CanCrossFade(stateHash)) return;

        _currentHash = stateHash;
        _animator.CrossFade(stateHash, transitionDuration);
    }

    public void ForcePlay(string stateName, float transitionDuration)
    {
        if (string.IsNullOrWhiteSpace(stateName)) return;
        RegisterStateName(stateName);
        ForcePlay(Animator.StringToHash(stateName), transitionDuration);
    }

    public void ForcePlay(int stateHash, float transitionDuration)
    {
        if (_animator == null || stateHash == 0) return;
        if (!CanCrossFade(stateHash)) return;

        _currentHash = stateHash;
        _animator.CrossFade(stateHash, transitionDuration, 0, 0f);
    }

    public void PlayHitReaction(in HitReactionAnimSet set, HitReactionKind kind)
    {
        string stateName = set.Resolve(kind);
        if (string.IsNullOrWhiteSpace(stateName)) return;
        ForcePlay(stateName, set.Transition);
    }

    public void RegisterStateName(string stateName)
    {
        if (string.IsNullOrWhiteSpace(stateName)) return;
        int stateHash = Animator.StringToHash(stateName);
        if (!_stateNamesByHash.ContainsKey(stateHash))
            _stateNamesByHash.Add(stateHash, stateName);
    }

    public void RegisterStateNames(params string[] stateNames)
    {
        if (stateNames == null) return;

        foreach (string stateName in stateNames)
            RegisterStateName(stateName);
    }

    public bool HasCurrentStateReachedEnd(string stateName)
    {
        if (_animator == null || string.IsNullOrWhiteSpace(stateName)) return true;
        if (_animator.IsInTransition(0)) return false;

        AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
        int hash = Animator.StringToHash(stateName);
        return stateInfo.shortNameHash == hash && stateInfo.normalizedTime >= 1f;
    }

    public static float ResolveSpeed(ActorAnimationTimeDomain timeDomain)
    {
        if (Main.Loop == null)
            return 1f;

        return timeDomain switch
        {
            ActorAnimationTimeDomain.World => Main.Loop.WorldTimeScale,
            ActorAnimationTimeDomain.Player => Main.Loop.PlayerTimeScale,
            _ => 1f
        };
    }

    private bool CanCrossFade(int stateHash)
    {
        RuntimeAnimatorController controller = _animator.runtimeAnimatorController;
        if (controller == null)
        {
            if (_loggedNoControllerHashes.Add(stateHash))
                Debug.LogWarning(
                    $"Animator has no AnimatorController. Requested animation key={DescribeState(stateHash)} animator={DescribeAnimator()}",
                    _animator);
            return false;
        }

        if (!HasState(stateHash) && _loggedMissingStateHashes.Add(stateHash))
        {
            Debug.LogWarning(
                $"AnimatorController does not contain requested animation key={DescribeState(stateHash)} controller='{controller.name}' animator={DescribeAnimator()}",
                _animator);
        }

        return true;
    }

    private bool HasState(int stateHash)
    {
        if (_animator.HasState(0, stateHash))
            return true;

        if (!_stateNamesByHash.TryGetValue(stateHash, out string stateName))
            return false;

        string layerName = _animator.GetLayerName(0);
        if (!string.IsNullOrWhiteSpace(layerName) && !stateName.Contains("."))
            return _animator.HasState(0, Animator.StringToHash($"{layerName}.{stateName}"));

        return false;
    }

    private string DescribeState(int stateHash)
    {
        if (_stateNamesByHash.TryGetValue(stateHash, out string stateName))
            return $"'{stateName}' hash={stateHash}";

        return $"hash={stateHash}";
    }

    private string DescribeAnimator()
    {
        GameObject gameObject = _animator.gameObject;
        return $"'{GetPath(gameObject.transform)}'";
    }

    private static string GetPath(Transform transform)
    {
        if (transform == null) return "<null>";

        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = $"{transform.name}/{path}";
        }

        return path;
    }
}
