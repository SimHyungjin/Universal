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
    private Character_MoveController _moveController;
    private Character_CommandSource _commandSource;
    private readonly ActorAnimationPlayback _animation = new();

    private void Awake()
    {
        animator ??= GetComponentInChildren<Animator>(true);
        _moveController = GetComponent<Character_MoveController>();
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
        if (_isAttacking || _suppressLocomotion) return;

        bool moving = IsMoving();
        if (_holdIdleDuringComboWindow && !moving)
            return;

        if (_jumpIdleStateHash != 0 && _moveController != null && !_moveController.IsGrounded)
        {
            _animation.Play(_jumpIdleStateHash, ActionTransition);
            return;
        }

        _animation.TickLocomotion(moving, gdt, _locomotion);
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
            jumpIdle);
    }

    private string JumpIdleStateName => _data != null ? _data.JumpIdleStateName : jumpIdleStateName;
    private float ActionTransition => _data != null ? _data.ActionTransition : 0.05f;
    private string IdleStateName => _data != null ? _data.IdleStateName : idleStateName;
    private string RunStateName => _data != null ? _data.RunStateName : runStateName;
    private float StartRunTransition => _data != null ? _data.StartRunTransition : startRunTransition;
    private float StopRunTransition => _data != null ? _data.StopRunTransition : stopRunTransition;
    private float RunEnterDelay => _data != null ? _data.RunEnterDelay : runEnterDelay;
    private float MinRunDuration => _data != null ? _data.MinRunDuration : minRunDuration;
    private float MoveThreshold => _data != null ? _data.MoveThreshold : moveThreshold;
}
