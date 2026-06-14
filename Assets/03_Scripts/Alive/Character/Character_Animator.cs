using UnityEngine;

[DisallowMultipleComponent]
public class Character_Animator : LoopMonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private ActorAnimationTimeDomain animationTimeDomain = ActorAnimationTimeDomain.Player;
    private string idleStateName = "Idle";
    private string runStateName = "Run";
    private string jumpIdleStateName = "";
    private float startRunTransition = 0.05f;
    private float stopRunTransition = 0.18f;
    private float runEnterDelay = 0.06f;
    private float minRunDuration = 0.18f;
    private float moveThreshold = 0.01f;

    private SO_Actor_AnimationData _data;
    private ActorLocomotionAnimation _locomotion;
    private int _jumpIdleStateHash;
    private bool _isAttacking;
    private bool _suppressLocomotion;
    private bool _holdIdleDuringComboWindow;
    private bool _wasAirborne;
    private bool _playingLanding;
    private int _lastJumpArcVersion;
    private Character_ActionHandler _actionHandler;
    private Character_CommandSource _commandSource;
    private readonly ActorAnimationPlayback _animation = new();

    // 공격 windup(예고) 구간에서 애니 재생속도를 늦추기 위한 배율. SyncSpeed가 매 프레임 speed를
    // 덮어쓰므로 그 직후에 곱한다. 1=정상, <1=느림. Character_AttackController가 windup 동안만 설정.
    private float _attackSpeedScale = 1f;
    public void SetAttackSpeedScale(float scale) => _attackSpeedScale = Mathf.Clamp(scale, 0.01f, 1f);

    private void Awake()
    {
        animator ??= GetComponentInChildren<Animator>(true);
        _actionHandler = GetComponent<Character_ActionHandler>();
        _commandSource = GetComponent<Character_CommandSource>();
        _animation.Bind(animator);
        CacheHashes();
    }

    public void SetAnimationData(SO_Actor_AnimationData data)
    {
        if (data != null)
            _data = data;
        CacheHashes();
    }

    public void SetCommandSource(Character_CommandSource commandSource)
    {
        _commandSource = commandSource;
    }

    public void PlayAttack(AttackAnimationData data)
    {
        if (animator == null || string.IsNullOrWhiteSpace(data.stateName)) return;

        _isAttacking = true;
        _suppressLocomotion = true;
        _holdIdleDuringComboWindow = false;
        _animation.ResetLocomotionTimers();
        _animation.ForcePlay(data.stateName, data.transition);
    }

    public void PlayAction(string stateName, float transition)
    {
        _isAttacking = false;
        _suppressLocomotion = true;
        _holdIdleDuringComboWindow = false;
        _animation.ResetLocomotionTimers();
        _animation.ForcePlay(stateName, transition);
    }

    public void PlayHitReaction(HitReactionKind kind)
    {
        _isAttacking = false;
        _suppressLocomotion = true;
        _holdIdleDuringComboWindow = false;
        if (animator == null || _data == null) return;

        _animation.ResetLocomotionTimers();
        _animation.PlayHitReaction(_data.BuildHitReactionSet(), kind);
    }

    public bool HasCurrentStateReachedEnd(string stateName)
        => _animation.HasCurrentStateReachedEnd(stateName);

    public void ExitAttack(bool playIdle = true)
    {
        if (!_isAttacking) return;

        _isAttacking = false;
        if (!playIdle)
        {
            _suppressLocomotion = false;
            _holdIdleDuringComboWindow = true;
            _animation.ResetLocomotionTimers();
            return;
        }

        if (animator == null) return;

        _holdIdleDuringComboWindow = false;
        _animation.ResetLocomotionTimers();
        _animation.Play(_locomotion.IdleHash, StopRunTransition);
    }

    public void ReleaseLocomotion()
    {
        _isAttacking = false;
        _suppressLocomotion = false;
        _holdIdleDuringComboWindow = false;
    }

    protected override void OnGameUpdate(float gdt)
    {
        base.OnGameUpdate(gdt);
        if (animator == null) return;

        _animation.SyncSpeed(animationTimeDomain);
        if (_attackSpeedScale != 1f)
            animator.speed *= _attackSpeedScale;
        if (_isAttacking || _suppressLocomotion)
        {
            // 전신 동작(공격/대시/피격/사망)이 애니를 점유 중. 공중/착지 추적도 리셋.
            _wasAirborne = false;
            _playingLanding = false;
            return;
        }

        // 공중 포즈(JumpStart→JumpIdle)의 단일 권한. ActionHandler의 논리적 IsAirborne로 판단한다
        // (raw isGrounded 아님 → 착지 직후 플리커로 JumpIdle이 다시 끼어드는 경합이 구조적으로 불가능).
        if (_actionHandler != null && _actionHandler.IsAirborne)
        {
            _wasAirborne = true;
            _playingLanding = false;
            PlayJumpPose();
            return;
        }

        bool moving = IsMoving();

        // 착지 에지(공중→지상): Jump_End를 재생하고 클립이 끝나거나 이동/행동으로 취소될 때까지 유지한다.
        // 게임플레이 착지락(JumpLandingRecoveryTime)과 분리 — 락이 0이어도 착지 모션은 끝까지 보인다.
        if (_wasAirborne)
        {
            _wasAirborne = false;
            if (!string.IsNullOrWhiteSpace(JumpEndStateName))
            {
                _animation.Play(JumpEndStateName, ActionTransition);
                _playingLanding = true;
            }
        }

        if (_playingLanding)
        {
            if (!moving && !_animation.HasCurrentStateReachedEnd(JumpEndStateName))
                return;
            _playingLanding = false;
        }

        if (_holdIdleDuringComboWindow && !moving)
            return;

        _animation.TickLocomotion(moving, gdt, _locomotion);
    }

    // 이륙 직후 JumpStart, 그 클립이 끝나면 JumpIdle. JumpStart가 없으면 바로 JumpIdle.
    // _animation.Play는 멱등(같은 해시면 스킵)이라 매 프레임 호출해도 한 번만 크로스페이드된다.
    private void PlayJumpPose()
    {
        if (_jumpIdleStateHash == 0) return;

        string start = JumpStartStateName;
        bool hasStart = !string.IsNullOrWhiteSpace(start);

        // 새 점프 호(첫 점프·2단 점프 모두)면 처음부터 강제 재생한다. 안 그러면 2단 점프 때 이미 같은
        // JumpIdle 해시가 떠 있어 멱등 Play가 스킵 → 1단 점프 마지막 프레임이 그대로 고정된다.
        int arc = _actionHandler.JumpArcVersion;
        if (arc != _lastJumpArcVersion)
        {
            _lastJumpArcVersion = arc;
            _animation.ForcePlay(hasStart ? start : JumpIdleStateName, ActionTransition);
            return;
        }

        // 진행 중: JumpStart가 끝나면 JumpIdle로 넘어간다.
        if (hasStart
            && _animation.CurrentHash != _jumpIdleStateHash
            && !_animation.HasCurrentStateReachedEnd(start))
        {
            _animation.Play(start, ActionTransition);
            return;
        }

        _animation.Play(_jumpIdleStateHash, ActionTransition);
    }

    private bool IsMoving()
    {
        _commandSource ??= GetComponent<Character_CommandSource>();
        if (_commandSource != null)
            return _commandSource.MoveWorld.sqrMagnitude > MoveThreshold * MoveThreshold;

        return Main.Input != null
               && Main.Input.IsActive<InputActions_Move>()
               && InputProvider.Move.Direction.sqrMagnitude > MoveThreshold * MoveThreshold;
    }

    private void CacheHashes()
    {
        _locomotion = ActorLocomotionAnimation.FromStateNames(
            IdleStateName,
            RunStateName,
            StartRunTransition,
            StopRunTransition,
            RunEnterDelay,
            MinRunDuration);

        string jumpIdle = JumpIdleStateName;
        _jumpIdleStateHash = string.IsNullOrEmpty(jumpIdle) ? 0 : Animator.StringToHash(jumpIdle);

        _animation.RegisterStateNames(
            IdleStateName,
            RunStateName,
            jumpIdle,
            JumpStartStateName,
            JumpEndStateName);
    }

    private string JumpStartStateName => _data != null ? _data.JumpStartStateName : "";
    private string JumpIdleStateName => _data != null ? _data.JumpIdleStateName : jumpIdleStateName;
    private string JumpEndStateName => _data != null ? _data.JumpEndStateName : "";
    private float ActionTransition => _data != null ? _data.ActionTransition : 0.05f;
    private string IdleStateName => _data != null ? _data.IdleStateName : idleStateName;
    private string RunStateName => _data != null ? _data.RunStateName : runStateName;
    private float StartRunTransition => _data != null ? _data.StartRunTransition : startRunTransition;
    private float StopRunTransition => _data != null ? _data.StopRunTransition : stopRunTransition;
    private float RunEnterDelay => _data != null ? _data.RunEnterDelay : runEnterDelay;
    private float MinRunDuration => _data != null ? _data.MinRunDuration : minRunDuration;
    private float MoveThreshold => _data != null ? _data.MoveThreshold : moveThreshold;
}
