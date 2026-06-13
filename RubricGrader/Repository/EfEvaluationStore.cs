using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RubricGrader.Audit;
using RubricGrader.Domain;
using RubricGrader.Worker;

namespace RubricGrader.Repository;

/// <summary>
/// EF/Postgres store: persists the grade, every per-criterion score with its citation +
/// status, and the append-only audit ledger. Uses an IDbContextFactory so a singleton
/// (the worker's sink) can create a fresh context per unit of work.
///
/// TENANT ISOLATION lives here and only here (CLAUDE.md §3.6): every read query is
/// filtered by tenantId — see <see cref="TenantScoped"/>. Call sites can't bypass it
/// because they only have these tenant-parameterised methods, no raw DbContext access.
/// </summary>
public sealed class EfEvaluationStore : IEvaluationResultSink, IEvaluationReader, IRubricStore, IAuditStore
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<RubricDbContext> _factory;

    public EfEvaluationStore(IDbContextFactory<RubricDbContext> factory) => _factory = factory;

    // ---- writes -------------------------------------------------------------------

    public async Task SaveAsync(EvaluationResult result, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        db.Evaluations.Add(new EvaluationRow
        {
            Id = result.Id,
            TenantId = result.TenantId,
            ArtifactId = result.ArtifactId,
            RubricVersionId = result.RubricVersionId,
            State = result.State.ToString(),
            CompositeScore = result.CompositeScore,
            ModelId = result.ModelId,
            PromptVersion = result.PromptVersion,
            CreatedAt = DateTimeOffset.UtcNow,
            Criteria = result.Results.Select(r => new CriterionResultRow
            {
                Id = Guid.NewGuid().ToString(),
                EvaluationId = result.Id,
                TenantId = result.TenantId,
                CriterionKey = r.CriterionKey,
                Score = r.Score,
                Confidence = r.Confidence?.ToString(),
                EvidenceTurnId = r.EvidenceTurnId,
                EvidenceSpan = r.EvidenceSpan,
                ValidationStatus = r.ValidationStatus.ToString(),
            }).ToList(),
        });

        // Append-only: we only ever Add audit rows, never update/delete.
        foreach (var ev in AuditTrail.Build(result, DateTimeOffset.UtcNow))
            db.AuditEvents.Add(ToRow(ev));

        await db.SaveChangesAsync(ct);
    }

    public async Task AppendAsync(AuditEvent ev, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.AuditEvents.Add(ToRow(ev));   // append-only, like the grade ledger
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEvent>> GetEventsAsync(
        string tenantId, string subjectId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await TenantScoped(db.AuditEvents, tenantId)
            .Where(e => e.SubjectId == subjectId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToEvent).ToList();
    }

    private static AuditEventRow ToRow(AuditEvent ev) => new()
    {
        Id = ev.Id,
        TenantId = ev.TenantId,
        SubjectId = ev.SubjectId,
        Kind = ev.Kind,
        PayloadJson = ev.PayloadJson,
        CreatedAt = ev.CreatedAt,
    };

    private static AuditEvent ToEvent(AuditEventRow row) =>
        new(row.Id, row.TenantId, row.SubjectId, row.Kind, row.PayloadJson, row.CreatedAt);

    public async Task SaveRubricAsync(RubricVersion rubric, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var existing = await db.RubricVersions.FindAsync(new object?[] { rubric.Id }, ct);
        var json = JsonSerializer.Serialize(rubric.Criteria, JsonOpts);
        var flagsJson = rubric.FairnessFlags is null
            ? null
            : JsonSerializer.Serialize(rubric.FairnessFlags, JsonOpts);
        if (existing is null)
        {
            db.RubricVersions.Add(new RubricVersionRow
            {
                Id = rubric.Id,
                TenantId = rubric.TenantId,
                Version = rubric.Version,
                Status = rubric.Status.ToString(),
                CriteriaJson = json,
                ApprovedBy = rubric.ApprovedBy,
                ApprovedAt = rubric.ApprovedAt,
                FairnessFlagsJson = flagsJson,
            });
        }
        else
        {
            existing.Version = rubric.Version;
            existing.Status = rubric.Status.ToString();
            existing.CriteriaJson = json;
            existing.ApprovedBy = rubric.ApprovedBy;
            existing.ApprovedAt = rubric.ApprovedAt;
            existing.FairnessFlagsJson = flagsJson;
        }

        await db.SaveChangesAsync(ct);
    }

    // ---- tenant-scoped reads ------------------------------------------------------

    public async Task<RubricVersion?> GetRubricAsync(string tenantId, string rubricVersionId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await TenantScoped(db.RubricVersions, tenantId)
            .FirstOrDefaultAsync(r => r.Id == rubricVersionId, ct);
        return row is null ? null : ToRubric(row);
    }

    public async Task<EvaluationResult?> GetResultAsync(string tenantId, string evaluationId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await TenantScoped(db.Evaluations, tenantId)
            .Include(e => e.Criteria)
            .FirstOrDefaultAsync(e => e.Id == evaluationId, ct);
        return row is null ? null : ToResult(row);
    }

    public async Task<AuditChain?> GetAuditChainAsync(string tenantId, string evaluationId, CancellationToken ct = default)
    {
        var result = await GetResultAsync(tenantId, evaluationId, ct);
        if (result is null)
            return null;

        var rubric = await GetRubricAsync(tenantId, result.RubricVersionId, ct);
        return AuditChainBuilder.From(result, rubric);
    }

    /// <summary>The one tenant filter every read funnels through. Type-constrained to
    /// tenant-owned rows so a non-scoped query simply won't compile here.</summary>
    private static IQueryable<T> TenantScoped<T>(IQueryable<T> set, string tenantId)
        where T : class, ITenantOwned
        => set.Where(x => x.TenantId == tenantId);

    // ---- row -> domain mapping ----------------------------------------------------

    private static EvaluationResult ToResult(EvaluationRow row) => new(
        row.Id, row.TenantId, row.ArtifactId, row.RubricVersionId,
        Enum.Parse<EvaluationState>(row.State),
        row.CompositeScore,
        row.Criteria.Select(c => new CriterionResult(
            c.CriterionKey,
            c.Score,
            c.Confidence is null ? null : Enum.Parse<Confidence>(c.Confidence),
            c.EvidenceTurnId,
            c.EvidenceSpan,
            Enum.Parse<ValidationStatus>(c.ValidationStatus))).ToList(),
        row.ModelId, row.PromptVersion);

    private static RubricVersion ToRubric(RubricVersionRow row) => new(
        row.Id, row.TenantId, row.Version,
        Enum.Parse<RubricStatus>(row.Status),
        JsonSerializer.Deserialize<List<RubricCriterion>>(row.CriteriaJson, JsonOpts) ?? new(),
        row.ApprovedBy, row.ApprovedAt,
        row.FairnessFlagsJson is null
            ? null
            : JsonSerializer.Deserialize<List<FairnessFlag>>(row.FairnessFlagsJson, JsonOpts));
}
