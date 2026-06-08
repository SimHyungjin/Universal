using MapNav.Ecs;
using UnityEngine;

// 수직 운동(중력·수직속도·점프 호·launch)의 단일 소유자. 접지 판단·vertical cc.Move를 직접 수행하고,
// 행동 상태(공격/닷지/Normal)는 여기에 "요청"만 보낸다(StartJumpArc/CutJumpArcToFall/ApplyJumpImpulse 등).
//
// 계획 #2 Stage 2: 서브1=중력·수직속도 소유권 이관(완료). 서브2=점프 호 이관(이 변경).
// 이후 서브3에서 공중 공격 땜빵(SuspendsAtApex/Slam)을 수직 API로 흡수, 서브4=launch 이관.
[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public sealed class Character_VerticalMotion : MonoBehaviour
{
    private SO_WorldPhysics _worldPhysics;
    private CharacterController _cc;
    private float _verticalVelocity;

    // 점프 호(아케이드 아크). 상승은 EaseOutCubic 커브로 groundY 기준 y를 직접 구동, 정점 이후 중력 낙하.
    private bool _inJumpArc;
    private float _arcElapsed;
    private float _arcGroundY;
    private bool _arcFallingStarted;
    private float _arcHeight;
    private float _arcRiseTime;
    private float _arcAscentDuration;

    // launch(공중 부양). 잡몹 NavLaunchSystem과 동일한 LaunchPhysics(초기속도+중력)로 y를 구동해 궤적·체공을 통일.
    // 수평 넉백(_forcedDirection/_forcedSpeed)·pendingDown·착지 결과는 ActionHandler가 소유하고, 여기선 수직만.
    private float _launchVerticalVelocity;
    private float _launchGroundY;
    private float _launchHeight;
    private float _launchSuspendTimer;
    private float _launchElapsed;
    private float _launchMaxDuration;
    private static readonly RaycastHit[] LaunchGroundHits = new RaycastHit[8];
    private const float LaunchGroundProbeUp = 2f;
    private const float LaunchGroundProbeDown = 12f;
    private const float LaunchFailsafeExtraTime = 0.75f;
    private const float LaunchFailsafeMaxDuration = 3f;

    public float VerticalVelocity => _verticalVelocity;
    public bool InJumpArc => _inJumpArc;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
    }

    public void SetWorldPhysics(SO_WorldPhysics physics)
    {
        if (physics != null) _worldPhysics = physics;
    }

    public void SetVerticalVelocity(float velocity) => _verticalVelocity = velocity;

    // height만큼 솟구치는 상승 초기속도를 부여한다(v = sqrt(2·|g|·h)). 공격 SO의 셀프 점프 등에서 사용.
    public void ApplyJumpImpulse(float height)
    {
        _verticalVelocity = Mathf.Sqrt(Mathf.Max(0f, height) * -2f * Gravity);
    }

    // 접지 + 하강 중이면 지면 밀착 속도로 고정한 뒤 중력을 적분한다.
    public void TickGravity(bool grounded, float deltaTime)
    {
        if (grounded && _verticalVelocity < 0f)
            _verticalVelocity = GroundedStickVelocity;

        _verticalVelocity += Gravity * deltaTime;
    }

    // 공중 공격의 정점 체공 요청: 상승은 정점까지 허용하고 하강(vv<0)은 0으로 막아 체공시킨다(옛 MoveVerticalUntilApexThenSuspend).
    public void SuspendAtApexMove(float deltaTime)
    {
        if (_verticalVelocity > 0f)
            TickGravity(_cc.isGrounded, deltaTime);

        if (_verticalVelocity < 0f)
            _verticalVelocity = 0f;

        _cc.Move(new Vector3(0f, _verticalVelocity * deltaTime, 0f));
    }

    // 강하 공격 요청: 중력 무관하게 아래로 일정 속도 이동(옛 MoveController.MoveDown).
    public void SlamMove(float speed, float deltaTime)
    {
        _cc.Move(Vector3.down * Mathf.Abs(speed) * deltaTime);
    }

    // 점프 호 시작. height/riseTime/ascentDuration은 호출자(ActionHandler)가 SO에서 계산해 넘긴다.
    public void StartJumpArc(float height, float riseTime, float ascentDuration)
    {
        _inJumpArc = true;
        _arcElapsed = 0f;
        _arcGroundY = transform.position.y;
        _arcFallingStarted = false;
        _arcHeight = height;
        _arcRiseTime = Mathf.Max(0.01f, riseTime);
        _arcAscentDuration = Mathf.Max(_arcRiseTime, ascentDuration);
        _verticalVelocity = 0f;
    }

    // 공중 공격 등으로 상승을 끊고 즉시 낙하로 전환(기존 InterruptJumpArcForAttack 대체).
    public void CutJumpArcToFall()
    {
        if (!_inJumpArc) return;
        _arcElapsed = _arcAscentDuration;
        _arcFallingStarted = true;
    }

    // 매 프레임 호출. 착지하면 true(호 종료), 아니면 false.
    public bool TickJumpArc(float deltaTime)
    {
        _arcElapsed += deltaTime;

        if (_arcElapsed <= _arcAscentDuration)
        {
            float desiredY = _arcGroundY + EvaluateAscentHeight(_arcElapsed);
            _cc.Move(new Vector3(0f, desiredY - transform.position.y, 0f));
            return false;
        }

        if (!_arcFallingStarted)
        {
            _arcFallingStarted = true;
            _verticalVelocity = 0f;
        }

        TickGravity(_cc.isGrounded, deltaTime);
        _cc.Move(new Vector3(0f, _verticalVelocity * deltaTime, 0f));

        if (!_cc.isGrounded)
            return false;

        _inJumpArc = false;
        return true;
    }

    private float EvaluateAscentHeight(float elapsed)
    {
        if (elapsed <= _arcRiseTime)
        {
            float t = Mathf.Clamp01(elapsed / _arcRiseTime);
            return _arcHeight * EaseOutCubic(t);
        }

        return _arcHeight;
    }

    private static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    // launch 시작/refresh. 잡몹(NavLaunchSystem)과 동일한 LaunchPhysics로 y 구동. repeat 재타격 시
    // GroundY는 유지하고 수직속도만 재부여해 체공을 연장한다(잡몹 HitboxProcessor와 동일 규칙).
    public void StartLaunch(float height, float suspendDuration, bool wasLaunched)
    {
        bool startsNewArc = !wasLaunched || _launchVerticalVelocity <= 0f;
        if (!wasLaunched)
            _launchGroundY = ResolveLaunchGroundY(transform.position);
        if (startsNewArc)
            _launchElapsed = 0f;

        _launchHeight = Mathf.Max(0f, height);
        // launch 중력은 잡몹과 반드시 같은 값(LaunchPhysics.Gravity)을 써야 체공시간이 일치한다.
        float initialVelocity = LaunchPhysics.InitialVelocity(height, LaunchPhysics.Gravity);
        _launchVerticalVelocity = LaunchPhysics.RefreshVelocityForLaunchHit(_launchVerticalVelocity, initialVelocity, wasLaunched);
        _launchSuspendTimer = Mathf.Max(0f, suspendDuration);
        _launchMaxDuration = ResolveLaunchMaxDuration(_launchHeight, _launchSuspendTimer);
    }

    // 저글링 유지 시 체공 타이머만 연장(ReceiveHit의 launched+비-launch 후속타 분기).
    public void RefreshLaunchSuspend(float duration)
    {
        if (duration > 0f)
            _launchSuspendTimer = Mathf.Max(_launchSuspendTimer, duration);
    }

    // launch y 적분. horizontalDisplacement는 다음 평면 위치의 groundY 프로빙용. 수평+수직 단일 MoveDisplacement는
    // 호출자(ActionHandler)가 yDelta를 받아 합쳐 수행한다. 반환=착지 여부.
    public bool TickLaunchVertical(Vector3 horizontalDisplacement, float deltaTime, out float yDelta)
    {
        _launchElapsed += deltaTime;

        float y = transform.position.y;
        float ceiling = _launchGroundY + _launchHeight;
        LaunchPhysics.Integrate(ref y, ref _launchVerticalVelocity, LaunchPhysics.Gravity, deltaTime, ref _launchSuspendTimer, ceiling);

        Vector3 nextPlanarPosition = transform.position + new Vector3(horizontalDisplacement.x, 0f, horizontalDisplacement.z);
        float landingGroundY = ResolveLaunchGroundY(nextPlanarPosition);

        bool landed = _launchVerticalVelocity <= 0f && y <= landingGroundY;
        if (_launchMaxDuration > 0f && _launchElapsed >= _launchMaxDuration)
            landed = true;
        if (landed)
            y = landingGroundY;

        yDelta = y - transform.position.y;
        return landed;
    }

    private float ResolveLaunchGroundY(Vector3 samplePosition)
    {
        if (TryResolveLaunchGroundY(samplePosition, out float groundY))
            return groundY;

        return _launchGroundY != 0f ? _launchGroundY : transform.position.y;
    }

    private bool TryResolveLaunchGroundY(Vector3 samplePosition, out float groundY)
    {
        float radius = _cc != null ? Mathf.Max(0.05f, _cc.radius * 0.85f) : 0.25f;
        Vector3 origin = samplePosition + Vector3.up * LaunchGroundProbeUp;
        int count = Physics.SphereCastNonAlloc(
            origin,
            radius,
            Vector3.down,
            LaunchGroundHits,
            LaunchGroundProbeUp + LaunchGroundProbeDown,
            ~0,
            QueryTriggerInteraction.Ignore);

        float bestDistance = float.MaxValue;
        groundY = 0f;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = LaunchGroundHits[i];
            Collider hitCollider = hit.collider;
            LaunchGroundHits[i] = default;
            if (hitCollider == null || ShouldIgnoreLaunchGroundHit(hitCollider))
                continue;

            if (hit.distance >= bestDistance)
                continue;

            bestDistance = hit.distance;
            groundY = hit.point.y;
            found = true;
        }

        return found;
    }

    private bool ShouldIgnoreLaunchGroundHit(Collider hitCollider)
    {
        if (hitCollider.transform == transform || hitCollider.transform.IsChildOf(transform))
            return true;

        if (hitCollider.GetComponentInParent<Character_ActionHandler>() != null)
            return true;

        if (hitCollider.GetComponentInParent<Unit_NavVisualShell>() != null)
            return true;

        return false;
    }

    private static float ResolveLaunchMaxDuration(float height, float suspendDuration)
    {
        float initialVelocity = LaunchPhysics.InitialVelocity(height, LaunchPhysics.Gravity);
        float airtime = initialVelocity > 0f
            ? initialVelocity * 2f / Mathf.Abs(LaunchPhysics.Gravity)
            : 0f;
        return Mathf.Clamp(airtime + Mathf.Max(0f, suspendDuration) + LaunchFailsafeExtraTime, 0.35f, LaunchFailsafeMaxDuration);
    }

    private float Gravity => _worldPhysics != null ? _worldPhysics.Gravity : -15f;
    private float GroundedStickVelocity => _worldPhysics != null ? _worldPhysics.GroundedStickVelocity : -1f;
}
