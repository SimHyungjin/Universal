using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class Window_AttackDataReport : EditorWindow
{
    private readonly Vector2 _scrollReset = Vector2.zero;
    private Vector2 _scroll;
    private string _report = "";

    [MenuItem("Tools/Combat/Attack Data Report")]
    public static void Open()
    {
        Window_AttackDataReport window = GetWindow<Window_AttackDataReport>("Attack Data Report");
        window.RefreshReport();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Attack Data Report", EditorStyles.boldLabel);
        EditorGUILayout.Space(6f);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Refresh", GUILayout.Width(100f)))
                RefreshReport();

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_report)))
            {
                if (GUILayout.Button("Copy", GUILayout.Width(100f)))
                    EditorGUIUtility.systemCopyBuffer = _report;
            }
        }

        EditorGUILayout.Space(6f);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private void RefreshReport()
    {
        AttackDataReport report = BuildReport();
        _report = report.ToText();
        _scroll = _scrollReset;
        Debug.Log(_report);
        Repaint();
    }

    private static AttackDataReport BuildReport()
    {
        AttackDataReport report = new();
        string[] guids = AssetDatabase.FindAssets("t:SO_Attack_Data");
        report.Total = guids.Length;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            SO_Attack_Data attack = AssetDatabase.LoadAssetAtPath<SO_Attack_Data>(path);
            if (attack == null)
                continue;

            AnalyzeAttack(attack, path, report);
        }

        return report;
    }

    private static void AnalyzeAttack(SO_Attack_Data attack, string path, AttackDataReport report)
    {
        string id = $"{attack.name} ({path})";

        if (attack.SchemaVersion < SO_Attack_Data.CurrentSchemaVersion)
            report.Errors.Add($"{id}: schemaVersion {attack.SchemaVersion} is older than required {SO_Attack_Data.CurrentSchemaVersion}");
        if (!attack.HasDeliveryEvents)
            report.Errors.Add($"{id}: deliveryEvents is required; runtime legacy fallback is disabled");

        if (attack.HasDeliveryEvents)
            report.NewDeliveryEvents.Add(id);
        if (attack.HasMovementEvents)
            report.NewMovementEvents.Add(id);
        if (attack.HasFeedbackEvents)
            report.NewFeedbackEvents.Add(id);

        if (string.IsNullOrWhiteSpace(attack.Animation.stateName))
            report.Errors.Add($"{id}: animation stateName is empty");
        if (attack.Animation.timingMode != AttackAnimationTimingMode.PlayAtNormalSpeed
            || attack.Animation.startNormalizedTime > 0f)
            report.Unsupported.Add($"{id}: animation supports only PlayAtNormalSpeed from normalized time 0");

        AnalyzeNewSchema(attack, id, report);
    }

    private static void AnalyzeNewSchema(SO_Attack_Data attack, string id, AttackDataReport report)
    {
        if (!attack.HasDeliveryEvents && !attack.HasMovementEvents && !attack.HasFeedbackEvents)
            return;

        float totalDuration = attack.TotalDuration;
        if (totalDuration <= 0f)
            report.Errors.Add($"{id}: totalDuration <= 0");

        AnalyzeDeliveryEvents(attack, id, report, Mathf.Max(0f, totalDuration));
        AnalyzeMovementEvents(attack, id, report, Mathf.Max(0f, totalDuration));
        AnalyzeFeedbackEvents(attack, id, report, Mathf.Max(0f, totalDuration));
    }

    private static void AnalyzeDeliveryEvents(SO_Attack_Data attack, string id, AttackDataReport report, float totalDuration)
    {
        AttackDeliveryEvent[] deliveries = attack.DeliveryEvents;
        if (deliveries == null || deliveries.Length == 0)
        {
            if (attack.HasMovementEvents)
                report.Warnings.Add($"{id}: movementEvents exists but deliveryEvents is empty");
            return;
        }

        int enabledCount = 0;
        for (int i = 0; i < deliveries.Length; i++)
        {
            AttackDeliveryEvent delivery = deliveries[i];
            string label = DeliveryLabel(id, delivery, i);
            if (!delivery.enabled)
                continue;

            enabledCount++;
            if (delivery.startTime > totalDuration)
                report.Errors.Add($"{label}: startTime is after totalDuration");
            float activeWindow = delivery.type switch
            {
                AttackDeliveryType.Melee => delivery.melee.repeat.duration,
                AttackDeliveryType.Field => delivery.field.lifetime,
                _ => 0f
            };
            if (activeWindow > 0f && delivery.startTime + activeWindow > totalDuration)
                report.Warnings.Add($"{label}: active window extends past totalDuration");
            if (delivery.hitResult.damage < 0f)
                report.Errors.Add($"{label}: hitResult.damage < 0");
            if (delivery.hitResult.hitType == HitType.None && delivery.hitResult.damage > 0f)
                report.Warnings.Add($"{label}: damaging hit uses HitType.None");

            switch (delivery.type)
            {
                case AttackDeliveryType.Projectile:
                    if (delivery.dedupe == AttackHitDeduplication.OncePerAttack)
                        report.Unsupported.Add($"{label}: projectile does not support OncePerAttack across spawned projectiles");
                    if (delivery.projectile.pierce && delivery.dedupe == AttackHitDeduplication.None)
                        report.Errors.Add($"{label}: piercing projectile requires dedupe OncePerDeliveryEvent");
                    AnalyzeProjectileDelivery(delivery.projectile, label, report);
                    break;
                case AttackDeliveryType.Field:
                    AnalyzeFieldDelivery(delivery, deliveries, label, report);
                    break;
                default:
                    AnalyzeMeleeDelivery(delivery, label, report);
                    break;
            }
        }

        if (enabledCount == 0)
            report.Errors.Add($"{id}: deliveryEvents has no enabled event");
    }

    private static void AnalyzeMeleeDelivery(AttackDeliveryEvent delivery, string label, AttackDataReport report)
    {
        ValidateShape(delivery.melee.shape, label, report);
        if (delivery.melee.repeat.enabled)
        {
            if (delivery.melee.repeat.interval <= 0f)
                report.Errors.Add($"{label}: melee repeat enabled but interval <= 0");
            if (delivery.melee.repeat.duration <= 0f)
                report.Errors.Add($"{label}: melee repeat enabled but repeat.duration <= 0");
            if (delivery.dedupe == AttackHitDeduplication.None)
                report.Warnings.Add($"{label}: melee repeat has no dedupe");
        }
    }

    private static void AnalyzeProjectileDelivery(AttackProjectileDelivery projectile, string label, AttackDataReport report)
    {
        if (string.IsNullOrWhiteSpace(projectile.prefabAddress))
            report.Errors.Add($"{label}: projectile prefabAddress is empty");
        ValidateShape(projectile.shape, label, report);
        if (projectile.count <= 0)
            report.Errors.Add($"{label}: projectile count <= 0");
        if (projectile.speed <= 0f)
            report.Warnings.Add($"{label}: projectile speed <= 0");
        if (projectile.maxDistance <= 0f && projectile.lifetime <= 0f)
            report.Warnings.Add($"{label}: projectile has no maxDistance or lifetime");
    }

    private static void AnalyzeFieldDelivery(AttackDeliveryEvent delivery, AttackDeliveryEvent[] deliveries, string label, AttackDataReport report)
    {
        AttackFieldDelivery field = delivery.field;
        if (string.IsNullOrWhiteSpace(field.prefabAddress))
            report.Errors.Add($"{label}: field prefabAddress is empty");
        ValidateShape(field.shape, label, report);
        if (field.tickInterval <= 0f)
            report.Errors.Add($"{label}: field tickInterval <= 0");
        if (field.origin == FieldOrigin.ProjectileImpact && !HasEnabledProjectileDelivery(deliveries))
            report.Warnings.Add($"{label}: ProjectileImpact field requires a projectile delivery in the same attack");
        if (field.lifetime <= 0f)
            report.Warnings.Add($"{label}: field lifetime <= 0; field may not expire");
        if (delivery.dedupe != AttackHitDeduplication.None)
            report.Unsupported.Add($"{label}: field supports only dedupe None");
    }

    private static bool HasEnabledProjectileDelivery(AttackDeliveryEvent[] deliveries)
    {
        if (deliveries == null) return false;
        for (int i = 0; i < deliveries.Length; i++)
            if (deliveries[i].enabled && deliveries[i].type == AttackDeliveryType.Projectile)
                return true;
        return false;
    }

    private static void AnalyzeMovementEvents(SO_Attack_Data attack, string id, AttackDataReport report, float totalDuration)
    {
        AttackMovementEvent[] movements = attack.MovementEvents;
        if (movements == null || movements.Length == 0)
            return;

        for (int i = 0; i < movements.Length; i++)
        {
            AttackMovementEvent movement = movements[i];
            if (!movement.enabled)
                continue;

            string label = $"{id}: movementEvents[{i}] {movement.label}";
            if (movement.startTime > totalDuration)
                report.Errors.Add($"{label}: startTime is after totalDuration");
            if (movement.duration > 0f && movement.startTime + movement.duration > totalDuration)
                report.Warnings.Add($"{label}: movement window extends past totalDuration");

            switch (movement.type)
            {
                case AttackMovementType.SelfJump:
                    if (movement.height <= 0f)
                        report.Errors.Add($"{label}: SelfJump height <= 0");
                    break;
                case AttackMovementType.Slam:
                    if (movement.speed <= 0f)
                        report.Errors.Add($"{label}: Slam speed <= 0");
                    break;
                case AttackMovementType.Suspend:
                    break;
                default:
                    if (Mathf.Approximately(movement.distance, 0f))
                        report.Errors.Add($"{label}: movement distance is 0 (음수=후진 허용)");
                    if (movement.duration <= 0f)
                        report.Errors.Add($"{label}: movement duration <= 0");
                    break;
            }
        }
    }

    private static void AnalyzeFeedbackEvents(SO_Attack_Data attack, string id, AttackDataReport report, float totalDuration)
    {
        AttackFeedbackEvent[] events = attack.FeedbackEvents;
        if (events == null || events.Length == 0)
            return;

        int deliveryCount = attack.DeliveryEvents?.Length ?? 0;
        for (int i = 0; i < events.Length; i++)
        {
            AttackFeedbackEvent feedbackEvent = events[i];
            if (!feedbackEvent.enabled)
                continue;

            string label = $"{id}: feedbackEvents[{i}] {feedbackEvent.label}";
            if (feedbackEvent.trigger == AttackFeedbackTrigger.Timeline && feedbackEvent.startTime > totalDuration)
                report.Warnings.Add($"{label}: startTime is after totalDuration");
            if (feedbackEvent.trigger == AttackFeedbackTrigger.DeliveryFire
                && feedbackEvent.deliveryIndex >= deliveryCount)
                report.Errors.Add($"{label}: deliveryIndex is outside deliveryEvents");
            if (feedbackEvent.vfxOrigin == AttackFeedbackVfxOrigin.DeliveryCenter
                && feedbackEvent.trigger != AttackFeedbackTrigger.DeliveryFire)
                report.Errors.Add($"{label}: DeliveryCenter VFX requires DeliveryFire trigger");
            if (feedbackEvent.deferUntilWindupEnd && feedbackEvent.trigger != AttackFeedbackTrigger.Timeline)
                report.Unsupported.Add($"{label}: deferUntilWindupEnd is supported only for Timeline trigger");
            if (feedbackEvent.vfxOrigin == AttackFeedbackVfxOrigin.DeliveryCenter
                && feedbackEvent.trigger == AttackFeedbackTrigger.DeliveryFire
                && TargetsNonMeleeDelivery(feedbackEvent.deliveryIndex, attack.DeliveryEvents))
                report.Unsupported.Add($"{label}: DeliveryCenter resolves accurately only for melee delivery");
            if (feedbackEvent.localPlayerOnly && feedbackEvent.sfx != SfxType.None)
                report.Warnings.Add($"{label}: localPlayerOnly suppresses SFX for AI attacks");
            if (feedbackEvent.endTime > 0f)
            {
                if (feedbackEvent.trigger == AttackFeedbackTrigger.Timeline && feedbackEvent.endTime <= feedbackEvent.startTime)
                    report.Errors.Add($"{label}: endTime must be after startTime");
                if (feedbackEvent.endTime > totalDuration)
                    report.Warnings.Add($"{label}: endTime is after totalDuration (attack-end cleanup already despawns it)");
                if (feedbackEvent.vfxOrigin != AttackFeedbackVfxOrigin.Actor && !feedbackEvent.motionAfterimages)
                    report.Unsupported.Add($"{label}: endTime despawn only affects Actor-space VFX; World/DeliveryCenter VFX manage their own lifetime");
            }
            if (feedbackEvent.motionAfterimages && feedbackEvent.trigger != AttackFeedbackTrigger.Timeline)
                report.Unsupported.Add($"{label}: motionAfterimages expects Timeline trigger (uses startTime/endTime window)");
        }
    }

    private static bool TargetsNonMeleeDelivery(int deliveryIndex, AttackDeliveryEvent[] deliveries)
    {
        if (deliveries == null || deliveries.Length == 0)
            return false;
        if (deliveryIndex >= 0)
            return deliveryIndex < deliveries.Length && deliveries[deliveryIndex].type != AttackDeliveryType.Melee;

        for (int i = 0; i < deliveries.Length; i++)
            if (deliveries[i].enabled && deliveries[i].type != AttackDeliveryType.Melee)
                return true;
        return false;
    }

    private static void ValidateShape(AttackShapeData shape, string label, AttackDataReport report)
    {
        switch (shape.type)
        {
            case AttackShape.Box:
                if (shape.length <= 0f || shape.width <= 0f)
                    report.Errors.Add($"{label}: box shape requires length > 0 and width > 0");
                break;
            case AttackShape.Cone:
                if (Mathf.Max(shape.radius, shape.length) <= 0f)
                    report.Errors.Add($"{label}: cone shape requires radius or length > 0");
                if (shape.angle <= 0f)
                    report.Errors.Add($"{label}: cone shape angle <= 0");
                break;
            default:
                if (shape.radius <= 0f)
                    report.Errors.Add($"{label}: sphere shape radius <= 0");
                break;
        }
    }

    private static string DeliveryLabel(string id, AttackDeliveryEvent delivery, int index)
        => $"{id}: deliveryEvents[{index}] {delivery.label}";

    private sealed class AttackDataReport
    {
        public int Total;
        public readonly List<string> NewDeliveryEvents = new();
        public readonly List<string> NewMovementEvents = new();
        public readonly List<string> NewFeedbackEvents = new();
        public readonly List<string> Warnings = new();
        public readonly List<string> Errors = new();
        public readonly List<string> Unsupported = new();

        public string ToText()
        {
            StringBuilder sb = new();
            sb.AppendLine("Attack Data Report");
            sb.AppendLine("==================");
            sb.AppendLine($"Total SO_Attack_Data: {Total}");
            sb.AppendLine($"New deliveryEvents populated: {NewDeliveryEvents.Count}");
            sb.AppendLine($"New movementEvents populated: {NewMovementEvents.Count}");
            sb.AppendLine($"New feedbackEvents populated: {NewFeedbackEvents.Count}");
            sb.AppendLine($"Warnings: {Warnings.Count}");
            sb.AppendLine($"Errors: {Errors.Count}");
            sb.AppendLine($"Unsupported options: {Unsupported.Count}");

            AppendSection(sb, "Errors", Errors);
            AppendSection(sb, "Unsupported options", Unsupported);
            AppendSection(sb, "Warnings", Warnings);
            AppendSection(sb, "New deliveryEvents", NewDeliveryEvents);
            AppendSection(sb, "New movementEvents", NewMovementEvents);
            AppendSection(sb, "New feedbackEvents", NewFeedbackEvents);
            return sb.ToString();
        }

        private static void AppendSection(StringBuilder sb, string title, List<string> rows)
        {
            if (rows.Count == 0)
                return;

            sb.AppendLine();
            sb.AppendLine(title);
            sb.AppendLine(new string('-', title.Length));

            foreach (string row in rows)
                sb.AppendLine(row);
        }
    }
}
