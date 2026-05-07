using UnityEngine;

[DisallowMultipleComponent]
public class Player_Animator : LoopMonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private string idleStateName = "Idle";
    [SerializeField] private string runStateName = "Run";
    [SerializeField] private float startRunTransition = 0.05f;
    [SerializeField] private float stopRunTransition = 0.18f;
    [SerializeField] private float runEnterDelay = 0.06f;
    [SerializeField] private float minRunDuration = 0.18f;
    [SerializeField] private float moveThreshold = 0.01f;

    private int _idleStateHash;
    private int _runStateHash;
    private int _currentStateHash;
    private float _movingTime;
    private float _runTime;

    private void Awake()
    {
        animator ??= GetComponentInChildren<Animator>(true);
        _idleStateHash = Animator.StringToHash(idleStateName);
        _runStateHash = Animator.StringToHash(runStateName);
    }

    protected override void OnGameUpdate(float gdt)
    {
        base.OnGameUpdate(gdt);
        if (animator == null) return;

        bool isMoving = Main.Input.IsActive<InputActions_Move>()
            && InputProvider.Move.Direction.sqrMagnitude > moveThreshold * moveThreshold;

        if (isMoving)
        {
            _movingTime += gdt;
            _runTime += gdt;

            if (_currentStateHash != _runStateHash && _movingTime >= runEnterDelay)
            {
                _runTime = 0f;
                Play(_runStateHash, startRunTransition);
            }

            return;
        }

        _movingTime = 0f;

        if (_currentStateHash == _runStateHash)
        {
            _runTime += gdt;
            if (_runTime < minRunDuration) return;
        }

        Play(_idleStateHash, stopRunTransition);
    }

    private void Play(int stateHash, float transitionDuration)
    {
        if (_currentStateHash == stateHash) return;

        _currentStateHash = stateHash;
        animator.CrossFade(stateHash, transitionDuration);
    }
}
