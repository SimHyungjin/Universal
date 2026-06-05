using MapNav.Core;
using MapNav.Data;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MapNav.Ecs
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NavPathBuildSystem))]
    [BurstCompile]
    public partial struct NavMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<NavBlobReference>(out NavBlobReference navRef) || !navRef.Blob.IsCreated)
                return;

            NavContext ctx = new NavContext(navRef.Blob, navRef.LocalToWorld, navRef.WorldToLocal);
            float deltaTime = SystemAPI.Time.DeltaTime;

            new NavMovementJob
            {
                Ctx = ctx,
                DeltaTime = deltaTime
            }.ScheduleParallel();
        }
    }

    [BurstCompile]
    public partial struct NavMovementJob : IJobEntity
    {
        public NavContext Ctx;
        public float DeltaTime;

        public void Execute(
            in NavAgentSettings settings,
            ref NavAgentMotion motion,
            ref NavAgentTarget target,
            ref NavAgentPathRequest request,
            ref NavAgentPathStatus status,
            ref LocalTransform transform,
            in NavAgentSeparation separation,
            in NavAgentCombatTarget combat,
            in NavAgentKnockback knockback,
            in NavAgentAttack attack,
            in NavAgentLaunch launch,
            DynamicBuffer<NavAgentWaypoint> waypoints)
        {
            motion.RepathCooldownRemaining = math.max(0f, motion.RepathCooldownRemaining - DeltaTime);

            // 공중 부양 중에는 NavLaunchSystem이 y를 전담한다. xz 이동·height snap을 멈춰 y를 보존한다.
            if (launch.Airborne != 0)
            {
                Stop(ref motion);
                return;
            }

            if (NavKnockbackSystem.HasPlanarKnockbackVelocity(knockback.Velocity)
                || knockback.MotionLockTimer > 0f
                || knockback.WakeupTimer > 0f)
            {
                Stop(ref motion);
                ApplyHeightSnap(ref transform, in settings);
                return;
            }

            // 공격 동작(선딜·쿨다운) 중에는 제자리에 멈춘다. 위치를 옮기면 공격 애니가 미끄러진다.
            // 한 점 수렴은 추격 단계의 ring 정착(아래)이 막으므로 여기서 분리는 적용하지 않는다.
            if (attack.Phase != NavAttackPhase.Idle)
            {
                Stop(ref motion);
                ApplyHeightSnap(ref transform, in settings);
                return;
            }

            if (status.HasPath == 0 || waypoints.Length == 0)
            {
                Stop(ref motion);
                ApplyHeightSnap(ref transform, in settings);
                return;
            }

            float3 current = transform.Position;
            AdvancePastStaleWaypoints(ref motion, in settings, current, waypoints);

            if (motion.WaypointIndex >= waypoints.Length)
            {
                status.HasPath = 0;
                motion.WaypointIndex = 0;
                motion.LastWaypointAnchor = current;
                Stop(ref motion);
                ApplyHeightSnap(ref transform, in settings);
                return;
            }

            int index = motion.WaypointIndex;
            float3 wp = waypoints[index].Position;
            float3 steeringTarget = GetSteeringTarget(in settings, current, index, waypoints);
            float3 planarDelta = wp - current;
            planarDelta.y = 0f;

            float reachDistance = GetReachDistance(in settings, waypoints[index]);
            if (math.lengthsq(planarDelta) <= reachDistance * reachDistance)
            {
                index++;
                if (index >= waypoints.Length)
                {
                    status.HasPath = 0;
                    motion.WaypointIndex = 0;
                    motion.LastWaypointAnchor = current;
                    Stop(ref motion);
                    ApplyHeightSnap(ref transform, in settings);
                    return;
                }
                motion.WaypointIndex = index;
                motion.LastWaypointAnchor = current;
                wp = waypoints[index].Position;
                steeringTarget = GetSteeringTarget(in settings, current, index, waypoints);
                planarDelta = wp - current;
                planarDelta.y = 0f;
                reachDistance = GetReachDistance(in settings, waypoints[index]);
            }

            float distance = math.length(planarDelta);
            if (distance <= 1e-4f)
            {
                Stop(ref motion);
                ApplyHeightSnap(ref transform, in settings);
                return;
            }

            UpdateStuckState(ref motion, ref target, ref request, ref status, in settings, distance);
            if (target.Dirty != 0 || status.HasPath == 0)
            {
                status.HasPath = 0;
                waypoints.Clear();
                Stop(ref motion);
                ApplyHeightSnap(ref transform, in settings);
                return;
            }

            // 타겟 거리 기반 포위: 타겟에서 ring(=사거리 안) 거리에 정착시켜 한 점 수렴을 막는다.
            // ring 밖이면 ring까지만 전진, ring 안이면 가까울수록 기하급수적으로 강하게 후퇴한다.
            // 위치 변경은 이 추격 경로(IsMoving=1) 안에서만 일어나므로 비주얼이 미끄러지지 않는다.
            float ring = math.max(settings.AttackRange * settings.EncircleRingFactor, settings.AgentRadius * 2f);
            float3 toTarget = combat.HasTarget != 0 ? combat.Position - current : float3.zero;
            toTarget.y = 0f;
            float distToTarget = math.length(toTarget);

            float moveDistance;
            float3 step;
            bool retreating = combat.HasTarget != 0 && distToTarget > 1e-3f && distToTarget < ring;
            if (retreating)
            {
                // ring 안으로 밀려듦 → 타겟 반대로 후퇴(가까울수록 t²로 가속). 이웃 분리로 옆으로도 펼친다.
                float t = (ring - distToTarget) / ring;
                moveDistance = settings.MoveSpeed * DeltaTime * math.min(1f, t * t * settings.RetreatGain);
                float3 away = -toTarget / distToTarget;
                float3 sep = separation.Steering;
                sep.y = 0f;
                float3 dir = math.normalizesafe(away + sep * settings.SeparationStrength, away);
                step = dir * moveDistance;
            }
            else
            {
                moveDistance = math.min(settings.MoveSpeed * DeltaTime, distance);
                // ring 밖에서 추격: ring을 넘어 더 다가가지 않도록 전진량을 ring까지로 제한한다.
                if (combat.HasTarget != 0 && distToTarget > ring)
                    moveDistance = math.min(moveDistance, distToTarget - ring);

                float3 steering = NavAgentCore.ResolveSteering(steeringTarget, current, planarDelta);
                steering = ApplySeparationSteering(in settings, in separation, steering, planarDelta, waypoints[index].Required != 0);
                float3 direction = math.normalizesafe(steering, planarDelta / distance);
                step = direction * moveDistance;
                if (moveDistance > distance)
                    step = planarDelta;
            }

            float3 nextPosition = current + step;

            // 이 에이전트의 StepHeight를 ctx에 실어 밟기 가능한 장애물을 통과 허용한다.
            NavContext stepCtx = new NavContext(Ctx.Blob, Ctx.LocalToWorld, Ctx.WorldToLocal, settings.StepHeight);
            if (!TryResolveConstrainedPosition(
                    in stepCtx,
                    current,
                    nextPosition,
                    wp,
                    settings.AgentRadius,
                    settings.BoundaryTolerance,
                    reachDistance,
                    out nextPosition))
            {
                AccumulateBlockedRepath(ref motion, ref target, ref request, ref status, in settings);
                ApplyHeightSnap(ref transform, in settings);
                return;
            }

            transform.Position = nextPosition;

            // 후퇴(뒷걸음) 중에는 타겟을 바라본다(등지지 않게). 전진·분리 중에는 이동 방향을 본다
            // — 잡몹은 단방향 Run 애니라 회전이 이동 방향과 일치해야 게걸음처럼 미끄러지지 않는다.
            float3 faceDir = retreating ? toTarget / distToTarget : math.normalizesafe(step);
            if (math.lengthsq(faceDir) > 1e-4f)
                transform.Rotation = quaternion.LookRotationSafe(faceDir, math.up());

            motion.IsMoving = 1;
            motion.CurrentSpeed = DeltaTime > 0f ? moveDistance / DeltaTime : 0f;
            motion.Velocity = DeltaTime > 0f ? step / DeltaTime : float3.zero;

            ApplyHeightSnap(ref transform, in settings);
        }

        private static void Stop(ref NavAgentMotion motion)
        {
            motion.IsMoving = 0;
            motion.CurrentSpeed = 0f;
            motion.StuckTimer = 0f;
            motion.LastDistanceToWaypoint = 0f;
            motion.Velocity = float3.zero;
        }

        private void ApplyHeightSnap(ref LocalTransform transform, in NavAgentSettings settings)
        {
            // StepHeight를 ctx에 실어, 밟는 장애물 위에서는 그 윗면 높이로 스냅되도록 한다(밟고 올라감).
            NavContext ctx = new NavContext(Ctx.Blob, Ctx.LocalToWorld, Ctx.WorldToLocal, settings.StepHeight);
            if (NavAgentCore.TrySnapHeight(in ctx, transform.Position, settings.BoundaryTolerance, settings.HeightOffset, out float3 snapped))
                transform.Position = snapped;
        }

        private static void AdvancePastStaleWaypoints(
            ref NavAgentMotion motion,
            in NavAgentSettings settings,
            float3 current,
            DynamicBuffer<NavAgentWaypoint> waypoints)
        {
            while (motion.WaypointIndex < waypoints.Length)
            {
                NavAgentWaypoint wp = waypoints[motion.WaypointIndex];
                float3 toCurrent = wp.Position - current;
                toCurrent.y = 0f;
                float advanceDistance = GetReachDistance(in settings, wp);
                float advanceSqr = advanceDistance * advanceDistance;

                if (math.lengthsq(toCurrent) <= advanceSqr)
                {
                    motion.LastWaypointAnchor = wp.Position;
                    motion.WaypointIndex++;
                    continue;
                }

                bool passedWaypoint = HasPassedCurrentWaypoint(motion.LastWaypointAnchor, wp.Position, current);
                if (wp.Required == 0 && passedWaypoint)
                {
                    motion.LastWaypointAnchor = wp.Position;
                    motion.WaypointIndex++;
                    continue;
                }

                if (wp.Required != 0
                    && passedWaypoint
                    && IsCloserToNextWaypoint(current, motion.WaypointIndex, waypoints))
                {
                    motion.LastWaypointAnchor = wp.Position;
                    motion.WaypointIndex++;
                    continue;
                }

                break;
            }
        }

        private static bool HasPassedCurrentWaypoint(float3 anchor, float3 waypoint, float3 current)
            => NavAgentCore.HasPassedWaypoint(anchor, waypoint, current);

        private static bool IsCloserToNextWaypoint(
            float3 current,
            int index,
            DynamicBuffer<NavAgentWaypoint> waypoints)
        {
            int nextIndex = index + 1;
            if (nextIndex >= waypoints.Length)
                return false;

            float3 toCurrent = waypoints[index].Position - current;
            float3 toNext = waypoints[nextIndex].Position - current;
            toCurrent.y = 0f;
            toNext.y = 0f;
            return math.lengthsq(toNext) < math.lengthsq(toCurrent);
        }

        private static float GetReachDistance(in NavAgentSettings settings, in NavAgentWaypoint waypoint)
            => NavAgentCore.GetReachDistance(settings.StopDistance, settings.WaypointAdvanceDistance, waypoint.Required != 0);

        private static float3 GetSteeringTarget(
            in NavAgentSettings settings,
            float3 current,
            int index,
            DynamicBuffer<NavAgentWaypoint> waypoints)
        {
            bool hasNext = index + 1 < waypoints.Length;
            float3 nextWaypoint = hasNext ? waypoints[index + 1].Position : current;
            return NavAgentCore.GetSteeringTarget(current, waypoints[index].Position, nextWaypoint, hasNext, settings.CornerLookAheadDistance);
        }

        private static float3 ApplySeparationSteering(
            in NavAgentSettings settings,
            in NavAgentSeparation separation,
            float3 steering,
            float3 planarDelta,
            bool nearTransition = false)
        {
            if (settings.SeparationStrength <= 0f || math.lengthsq(separation.Steering) <= 1e-6f)
                return steering;

            if (nearTransition)
                return steering;

            float3 pathDir = math.normalizesafe(planarDelta);
            float3 separationSteering = separation.Steering;
            separationSteering.y = 0f;

            // 분리의 전진 저지 성분을 일부만 깎는다(과거 0.75는 추격 중 분리를 너무 약화시켜 한 점 수렴을 키웠다).
            float backward = math.min(0f, math.dot(separationSteering, pathDir));
            separationSteering -= pathDir * backward * 0.5f;

            float3 mixed = steering + separationSteering * settings.SeparationStrength;
            if (math.lengthsq(mixed) <= 1e-6f || math.dot(mixed, planarDelta) < -1e-4f)
                return steering;

            return mixed;
        }

        private void UpdateStuckState(
            ref NavAgentMotion motion,
            ref NavAgentTarget target,
            ref NavAgentPathRequest request,
            ref NavAgentPathStatus status,
            in NavAgentSettings settings,
            float distanceToWaypoint)
        {
            if (settings.StuckRepathDelay <= 0f || request.Pending != 0 || status.Waiting != 0)
            {
                motion.StuckTimer = 0f;
                motion.LastDistanceToWaypoint = distanceToWaypoint;
                return;
            }

            if (NavAgentCore.EvaluateProgressStuck(
                    ref motion.StuckTimer, ref motion.LastDistanceToWaypoint, motion.RepathCooldownRemaining,
                    distanceToWaypoint, settings.StuckRepathDelay, settings.StuckProgressDistance, settings.MoveSpeed, DeltaTime))
                TriggerStuckRepath(ref motion, ref target, ref request, ref status, in settings);
        }

        private void AccumulateBlockedRepath(
            ref NavAgentMotion motion,
            ref NavAgentTarget target,
            ref NavAgentPathRequest request,
            ref NavAgentPathStatus status,
            in NavAgentSettings settings)
        {
            if (settings.StuckRepathDelay <= 0f || request.Pending != 0 || status.Waiting != 0)
                return;

            if (NavAgentCore.EvaluateBlockedStuck(ref motion.StuckTimer, motion.RepathCooldownRemaining, settings.StuckRepathDelay, DeltaTime))
                TriggerStuckRepath(ref motion, ref target, ref request, ref status, in settings);
        }

        private static void TriggerStuckRepath(
            ref NavAgentMotion motion,
            ref NavAgentTarget target,
            ref NavAgentPathRequest request,
            ref NavAgentPathStatus status,
            in NavAgentSettings settings)
        {
            bool giveUp = NavAgentCore.CommitStuckRepath(
                ref motion.StuckTimer, ref motion.RepathCooldownRemaining, ref motion.StuckRetryCount,
                settings.StuckRepathCooldown, settings.StuckRetryLimit);

            if (giveUp)
            {
                request.Pending = 0;
                status.HasPath = 0;
                status.Waiting = 0;
                status.Failed = 1;
                target.Dirty = 0;
                motion.IsMoving = 0;
                motion.CurrentSpeed = 0f;
                motion.Velocity = float3.zero;
                return;
            }

            target.Position = target.AcceptedPosition;
            target.Dirty = 1;
            request.Pending = 0;
            status.HasPath = 0;
            status.Waiting = 0;
        }

        private static bool CanMoveToConstrainedPosition(
            in NavContext ctx,
            float3 current,
            float3 nextPosition,
            float3 waypoint,
            float agentRadius,
            float tolerance,
            float waypointReachDistance)
            => NavAgentCore.CanMove(in ctx, current, nextPosition, waypoint, agentRadius, tolerance, waypointReachDistance);

        private static bool TryResolveConstrainedPosition(
            in NavContext ctx,
            float3 current,
            float3 desired,
            float3 waypoint,
            float agentRadius,
            float tolerance,
            float waypointReachDistance,
            out float3 resolved)
        {
            resolved = desired;
            if (CanMoveToConstrainedPosition(in ctx, current, desired, waypoint, agentRadius, tolerance, waypointReachDistance))
                return true;

            float3 delta = desired - current;
            delta.y = 0f;
            float distance = math.length(delta);
            if (distance <= 1e-4f)
                return false;

            float3 forward = delta / distance;
            float3 sideA = new float3(-forward.z, 0f, forward.x);
            float3 sideB = new float3(forward.z, 0f, -forward.x);
            float3 progressDir = waypoint - current;
            progressDir.y = 0f;
            progressDir = math.normalizesafe(progressDir, forward);

            bool found = false;
            float bestScore = float.NegativeInfinity;
            resolved = current;

            ConsiderSlideCandidate(in ctx, current, forward + sideA * 0.8f, distance, progressDir, waypoint,
                agentRadius, tolerance, waypointReachDistance, ref found, ref bestScore, ref resolved);
            ConsiderSlideCandidate(in ctx, current, forward + sideB * 0.8f, distance, progressDir, waypoint,
                agentRadius, tolerance, waypointReachDistance, ref found, ref bestScore, ref resolved);
            ConsiderSlideCandidate(in ctx, current, sideA, distance * 0.75f, progressDir, waypoint,
                agentRadius, tolerance, waypointReachDistance, ref found, ref bestScore, ref resolved);
            ConsiderSlideCandidate(in ctx, current, sideB, distance * 0.75f, progressDir, waypoint,
                agentRadius, tolerance, waypointReachDistance, ref found, ref bestScore, ref resolved);
            ConsiderSlideCandidate(in ctx, current, forward, distance * 0.5f, progressDir, waypoint,
                agentRadius, tolerance, waypointReachDistance, ref found, ref bestScore, ref resolved);

            return found;
        }

        private static void ConsiderSlideCandidate(
            in NavContext ctx,
            float3 current,
            float3 direction,
            float distance,
            float3 progressDir,
            float3 waypoint,
            float agentRadius,
            float tolerance,
            float waypointReachDistance,
            ref bool found,
            ref float bestScore,
            ref float3 bestPosition)
        {
            direction.y = 0f;
            direction = math.normalizesafe(direction);
            if (math.lengthsq(direction) <= 1e-6f || distance <= 1e-4f)
                return;

            float3 candidate = current + direction * distance;
            if (!CanMoveToConstrainedPosition(in ctx, current, candidate, waypoint, agentRadius, tolerance, waypointReachDistance))
                return;

            float3 moved = candidate - current;
            moved.y = 0f;
            float score = math.dot(moved, progressDir) - math.lengthsq(moved - progressDir * math.dot(moved, progressDir)) * 0.15f;
            if (found && score <= bestScore)
                return;

            found = true;
            bestScore = score;
            bestPosition = candidate;
        }
    }
}
