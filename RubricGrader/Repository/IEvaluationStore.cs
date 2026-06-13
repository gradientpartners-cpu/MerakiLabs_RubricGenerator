using RubricGrader.Audit;
using RubricGrader.Domain;

namespace RubricGrader.Repository;

/// <summary>Tenant-scoped reads for the API. EVERY method takes a tenantId and the
/// implementation filters on it — that is the single enforcement point (CLAUDE.md §3.6).
/// There is no data-access path that doesn't go through a tenant-filtered query.</summary>
public interface IEvaluationReader
{
    Task<EvaluationResult?> GetResultAsync(string tenantId, string evaluationId, CancellationToken ct = default);

    /// <summary>The full reconstructable chain: composite -> each criterion score -> its
    /// cited evidence + validation status -> rubric version.</summary>
    Task<AuditChain?> GetAuditChainAsync(string tenantId, string evaluationId, CancellationToken ct = default);
}

/// <summary>Persistence for approved rubric versions — the immutable input the grader and
/// the audit chain both reference. Populated by seed/approve (the generator later).</summary>
public interface IRubricStore
{
    Task SaveRubricAsync(RubricVersion rubric, CancellationToken ct = default);
    Task<RubricVersion?> GetRubricAsync(string tenantId, string rubricVersionId, CancellationToken ct = default);
}

/// <summary>Append-only audit ledger access for events not tied to a grade run — chiefly
/// the rubric-approval event. Writes only ever append; reads are tenant-scoped through the
/// same chokepoint as every other read (CLAUDE.md §3.6).</summary>
public interface IAuditStore
{
    Task AppendAsync(AuditEvent ev, CancellationToken ct = default);

    /// <summary>All audit events about one subject (e.g. a rubric version), newest-stamped
    /// last, scoped to the caller's tenant.</summary>
    Task<IReadOnlyList<AuditEvent>> GetEventsAsync(string tenantId, string subjectId, CancellationToken ct = default);
}
