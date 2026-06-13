using System.Collections.Concurrent;
using RubricGrader.Audit;
using RubricGrader.Domain;
using RubricGrader.Worker;

namespace RubricGrader.Repository;

/// <summary>
/// Default store for the zero-credential demo: keeps everything in memory so the pipeline
/// + worker + API run end-to-end with NO database. Implements the same interfaces as the
/// EF store, so it is a drop-in for the persistence path. Tenant scoping is enforced the
/// same way as EF — every read is filtered by tenantId (CLAUDE.md §3.6).
/// </summary>
public sealed class InMemoryEvaluationStore : IEvaluationResultSink, IEvaluationReader, IRubricStore, IAuditStore
{
    private readonly ConcurrentDictionary<string, EvaluationResult> _evaluations = new();
    private readonly ConcurrentDictionary<string, RubricVersion> _rubrics = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<AuditEvent>> _auditLedger = new();
    // Subject-keyed append-only ledger for events not tied to a grade run (rubric approvals).
    private readonly ConcurrentDictionary<string, List<AuditEvent>> _subjectAudit = new();

    public Task SaveAsync(EvaluationResult result, CancellationToken ct = default)
    {
        _evaluations[result.Id] = result;
        // Append-only ledger, mirroring what the EF store persists.
        _auditLedger[result.Id] = AuditTrail.Build(result, DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    public Task SaveRubricAsync(RubricVersion rubric, CancellationToken ct = default)
    {
        _rubrics[rubric.Id] = rubric;
        return Task.CompletedTask;
    }

    public Task AppendAsync(AuditEvent ev, CancellationToken ct = default)
    {
        var list = _subjectAudit.GetOrAdd(ev.SubjectId, _ => new List<AuditEvent>());
        lock (list) list.Add(ev);   // append-only, never mutate prior entries
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEvent>> GetEventsAsync(
        string tenantId, string subjectId, CancellationToken ct = default)
    {
        IReadOnlyList<AuditEvent> events = _subjectAudit.TryGetValue(subjectId, out var list)
            ? list.Where(e => e.TenantId == tenantId).OrderBy(e => e.CreatedAt).ToList()
            : Array.Empty<AuditEvent>();
        return Task.FromResult(events);
    }

    public Task<RubricVersion?> GetRubricAsync(string tenantId, string rubricVersionId, CancellationToken ct = default)
        => Task.FromResult(
            _rubrics.TryGetValue(rubricVersionId, out var r) && r.TenantId == tenantId ? r : null);

    public Task<EvaluationResult?> GetResultAsync(string tenantId, string evaluationId, CancellationToken ct = default)
        => Task.FromResult(Find(tenantId, evaluationId));

    public async Task<AuditChain?> GetAuditChainAsync(string tenantId, string evaluationId, CancellationToken ct = default)
    {
        var result = Find(tenantId, evaluationId);
        if (result is null)
            return null;

        var rubric = await GetRubricAsync(tenantId, result.RubricVersionId, ct);
        return AuditChainBuilder.From(result, rubric);
    }

    /// <summary>The single tenant-enforcement point for reads: a result is only returned
    /// when its tenant matches the caller's.</summary>
    private EvaluationResult? Find(string tenantId, string evaluationId)
        => _evaluations.TryGetValue(evaluationId, out var e) && e.TenantId == tenantId ? e : null;
}
