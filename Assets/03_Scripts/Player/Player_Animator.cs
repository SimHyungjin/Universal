using UnityEngine;

[DisallowMultipleComponent]
public class Player_Animator : LoopMonoBehaviour
{
    [SerializeField] private Animator animator;
    private string idleStateName = "Idle";
    private string runStateName = "Run";
    private string jumpIdleStateName = "";
    private float startRunTransition = 0.05f;
    private float stopRunTransition = 0.18f;
    private float runEnterDelay = 0.06f;
    private float minRunDuration = 0.18f;
    private float moveThreshold = 0.01f;

    private SO_PlayerAnimationData _data;
    private int _idleStateHash;
    private int _runStateHash;
    private int _jumpIdleStateHash;
    private int _currentStateHash;
    private float _movingTime;
    private float _runTime;
    private bool _isAttacking;
    private bool _suppressLocomotion;
    private Player_Movecontroller _moveController;

    private void Awake()
    {
        animator ??= GetComponentInChildren<Animator>(true);
        _moveController = GetComponent<Player_Movecontroller>();
        // Time.timeScale에서 분리 — 월드 정지(timeScale=0) 중에도 플레이어 애니메이션은 재생.
        // 정지/감속은 animator.speed = PlayerTimeScale로 제어한다.
        if (animator != null)
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        CacheHashes();
    }

    public void SetAnimationData(SO_PlayerAnimationData data)
    {
        if (data != null)
            _data = data;
        CacheHashes();
    }

    public void PlayAttack(AttackAnimationData data)
    {
        if (animator == null || string.IsNullOrWhiteSpace(data.stateName)) return;

        _isAttacking = true;
        _suppressLocomotion = true;
        int hash = Animator.StringToHash(data.stateName);
        _currentStateHash = hash;
        animator.CrossFade(hash, data.transition);
    }

    public void PlayAction(string stateName, float transition)
    {
        _isAttacking = false;
        _suppressLocomotion = true;
        if (animator == null || string.IsNullOrWhiteSpace(stateName)) return;

        int hash = Animator.StringToHash(stateName);
        _currentStateHash = hash;
        animator.CrossFade(hash, transition);
    }

    public void PlayMomentaryAction(string stateName, float transition)
    {
        if (animator == null || string.IsNullOrWhiteSpace(stateName)) return;

        int hash = Animator.StringToHash(stateName);
        _currentStateHash = hash;
        animator.CrossFade(hash, transition);
    }

    // 공격 애니메이션 종료 → Idle로 전환, 로코모션 억제는 유지
    // 공격 애니메이션 종료 → Idle로 전환, 로코모션 억제는 유지
    public bool HasCurrentStateReachedEnd(string stateName)
    {
        if (animator == null || string.IsNullOrWhiteSpace(stateName)) return true;
        if (animator.IsInTransition(0)) return false;

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        int hash = Animator.StringToHash(stateName);
        return stateInfo.shortNameHash == hash && stateInfo.normalizedTime >= 1f;
    }

    public void ExitAttack()
    {
        if (!_isAttacking) return;
        if (animator == null) return;
        _isAttacking = false;
        _movingTime = 0f;
        _runTime = 0f;
        _currentStateHash = _idleStateHash;
        animator.CrossFade(_idleStateHash, StopRunTransition);
    }

    public void ReleaseLocomotion()
    {
        _isAttacking = false;
        _suppressLocomotion = false;
    }

    protected override void OnGameUpdate(float gdt)
    {
        base.OnGameUpdate(gdt);
        if (animator == null) return;
        animator.speed = Main.Loop.PlayerTimeScale;
        if (_isAttacking || _suppressLocomotion) return;

        if (_jumpIdleStateHash != 0 && _moveController != null && !_moveController.IsGrounded)
        {
            Play(_jumpIdleStateHash, ActionTransition);
            return;
        }

        bool isMoving = Main.Input.IsActive<InputActions_Move>()
            && InputProvider.Move.Direction.sqrMagnitude > MoveThreshold * MoveThreshold;

        if (isMoving)
        {
            _movingTime += gdt;
            _runTime += gdt;

            if (_currentStateHash != _runStateHash && _movingTime >= RunEnterDelay)
            {
                _runTime = 0f;
                Play(_runStateHash, StartRunTransition);
            }

            return;
        }

        _movingTime = 0f;

        if (_currentStateHash == _runStateHash)
        {
            _runTime += gdt;
            if (_runTime < MinRunDuration) return;
        }

        Play(_idleStateHash, StopRunTransition);
    }

    private void Play(int stateHash, float transitionDuration)
    {
        if (_currentStateHash == stateHash) return;

        _currentStateHash = stateHash;
        animator.CrossFade(stateHash, transitionDuration);
    }

    private void CacheHashes()
    {
        _idleStateHash = Animator.StringToHash(IdleStateName);
        _runStateHash = Animator.StringToHash(RunStateName);
        string jumpIdle = JumpIdleStateName;
        _jumpIdleStateHash = string.IsNullOrEmpty(jumpIdle) ? 0 : Animator.StringToHash(jumpIdle);
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
