using System.Text.Json;
using RubricGrader.Domain;

namespace RubricGrader.Audit;

/// <summary>
/// Builds the append-only audit ledger for one evaluation (CLAUDE.md §3.7). Pure: given a
/// result + a timestamp it returns the events to persist, deriving nothing from I/O. Each
/// criterion becomes one event carrying its score, cited evidence, and validation status,
/// plus a summary event tying the composite to the rubric version + prompt + model — so a
/// grade is reconstructable from the ledger alone.
/// </summary>
public static class AuditTrail
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public const string CriterionScored = "criterion.scored";
    public const string EvaluationCompleted = "evaluation.completed";
    public const string RubricApproved = "rubric.approved";

    /// <summary>The audit event for a human freezing a Draft rubric into an Approved
    /// version. The human-approval gate IS the fairness enforcement point (DESIGN.md §8),
    /// so it must itself be traceable: this records WHO approved, WHEN, and the
    /// draft->approved transition, against the rubric version id as the subject. Without
    /// this, "a human approved it" would be an unverifiable claim.</summary>
    public static AuditEvent RubricApprovedEvent(RubricVersion approved, string approver, DateTimeOffset at)
    {
        var payload = JsonSerializer.Serialize(new
        {
            rubricVersionId = approved.Id,
            version = approved.Version,
            fromStatus = RubricStatus.Draft.ToString(),
            toStatus = approved.Status.ToString(),
            approvedBy = approver,
            approvedAt = at,
        }, JsonOpts);

        return new AuditEvent(
            Guid.NewGuid().ToString(), approved.TenantId, approved.Id,
            RubricApproved, payload, at);
    }

    public static IReadOnlyList<AuditEvent> Build(EvaluationResult result, DateTimeOffset createdAt)
    {
        var events = new List<AuditEvent>(result.Results.Count + 1);

        foreach (var c in result.Results)
        {
            var payload = JsonSerializer.Serialize(new
            {
                criterionKey = c.CriterionKey,
                score = c.Score,
                confidence = c.Confidence?.ToString(),
                evidenceTurnId = c.EvidenceTurnId,
                evidenceSpan = c.EvidenceSpan,
                validationStatus = c.ValidationStatus.ToString(),
            }, JsonOpts);

            events.Add(new AuditEvent(
                Guid.NewGuid().ToString(), result.TenantId, result.Id,
                CriterionScored, payload, createdAt));
        }

        var summary = JsonSerializer.Serialize(new
        {
            state = result.State.ToString(),
            compositeScore = result.CompositeScore,
            rubricVersionId = result.RubricVersionId,
            modelId = result.ModelId,
            promptVersion = result.PromptVersion,
        }, JsonOpts);

        events.Add(new AuditEvent(
            Guid.NewGuid().ToString(), result.TenantId, result.Id,
            EvaluationCompleted, summary, createdAt));

        return events;
    }
}
