using RubricGrader.Domain;

namespace RubricGrader.Audit;

/// <summary>Human-readable reconstruction of one decision (CLAUDE.md §3.7): the composite,
/// then every criterion's score with its cited evidence + validation status, traced back
/// to the exact rubric version that defined it. This is what GET /audit returns.</summary>
public sealed record AuditChain(
    string EvaluationId,
    string TenantId,
    string State,
    double? CompositeScore,
    string ModelId,
    string PromptVersion,
    RubricRef RubricVersion,
    IReadOnlyList<AuditChainCriterion> Criteria);

public sealed record RubricRef(string Id, int? Version, string? Status);

public sealed record AuditChainCriterion(
    string CriterionKey,
    string? Label,
    double? Weight,
    int? Score,
    string? Confidence,
    string? EvidenceTurnId,
    string? EvidenceSpan,
    string ValidationStatus);

public static class AuditChainBuilder
{
    /// <summary>Compose the chain from a stored result and (optionally) its rubric version.
    /// Criterion label + weight are joined from the rubric by key; if the rubric isn't
    /// available the chain still reconstructs from the result alone.</summary>
    public static AuditChain From(EvaluationResult result, RubricVersion? rubric)
    {
        var byKey = rubric?.Criteria.ToDictionary(c => c.Key);

        var criteria = result.Results.Select(r =>
        {
            RubricCriterion? def = null;
            byKey?.TryGetValue(r.CriterionKey, out def);
            return new AuditChainCriterion(
                r.CriterionKey,
                def?.Label,
                def?.Weight,
                r.Score,
                r.Confidence?.ToString(),
                r.EvidenceTurnId,
                r.EvidenceSpan,
                r.ValidationStatus.ToString());
        }).ToList();

        return new AuditChain(
            result.Id,
            result.TenantId,
            result.State.ToString(),
            result.CompositeScore,
            result.ModelId,
            result.PromptVersion,
            new RubricRef(result.RubricVersionId, rubric?.Version, rubric?.Status.ToString()),
            criteria);
    }
}
