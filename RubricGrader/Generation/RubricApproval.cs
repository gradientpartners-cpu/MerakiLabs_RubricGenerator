using RubricGrader.Audit;
using RubricGrader.Domain;
using RubricGrader.Repository;

namespace RubricGrader.Generation;

/// <summary>
/// The human-approval gate that freezes a Draft rubric into an immutable Approved version
/// — the fairness enforcement point (DESIGN.md §8). Kept separate from the HTTP endpoint so
/// the rule (and its audit guarantee) is unit-testable without a web host, mirroring the
/// grade pipeline's separability.
///
/// Approval is the moment a human becomes accountable for the rubric, so it MUST leave a
/// trace: every approval appends a <c>rubric.approved</c> event (approver + timestamp +
/// draft->approved transition) to the same append-only audit ledger. That closes the chain
/// — a score -> its rubric version -> the human-accountable approval that froze it.
/// </summary>
public static class RubricApproval
{
    public sealed class AlreadyApprovedException : Exception
    {
        public AlreadyApprovedException(string rubricId)
            : base($"Rubric '{rubricId}' is already approved; approved versions are immutable.") { }
    }

    /// <summary>Freeze the Draft identified by <paramref name="rubricId"/> for the caller's
    /// tenant. Returns null if no such draft exists for this tenant (caller -> 404); throws
    /// <see cref="AlreadyApprovedException"/> if it is already frozen (caller -> 409).</summary>
    public static async Task<RubricVersion?> ApproveAsync(
        IRubricStore rubrics, IAuditStore audit,
        string tenantId, string rubricId, string approver,
        CancellationToken ct = default)
    {
        var existing = await rubrics.GetRubricAsync(tenantId, rubricId, ct);
        if (existing is null)
            return null;   // not found for this tenant (tenant scoping enforced in the store)

        if (existing.Status == RubricStatus.Approved)
            throw new AlreadyApprovedException(rubricId);

        var at = DateTimeOffset.UtcNow;
        var approved = existing with
        {
            Status = RubricStatus.Approved,
            ApprovedBy = approver,
            ApprovedAt = at,
        };

        await rubrics.SaveRubricAsync(approved, ct);
        await audit.AppendAsync(AuditTrail.RubricApprovedEvent(approved, approver, at), ct);
        return approved;
    }
}
