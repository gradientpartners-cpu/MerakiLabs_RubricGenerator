using RubricGrader.Adapters;
using RubricGrader.Domain;

namespace RubricGrader.Grading;

/// <summary>Everything the pipeline needs to grade one artifact against one rubric. It
/// carries the rubric + transcript by value, so grading is fully decoupled from the
/// database — the worker fetches these, the pipeline never touches persistence.</summary>
public sealed record GradeRequest(
    string EvaluationId,
    string TenantId,
    string ArtifactId,
    RubricVersion Rubric,
    IReadOnlyList<TranscriptTurn> Turns,
    string ModelId);

/// <summary>
/// The grade pipeline: redact -> per-criterion prompt -> LLM adapter -> validate ->
/// deterministic aggregate -> EvaluationResult. A self-contained, side-effect-free unit
/// (CLAUDE.md §5): given inputs + an <see cref="ILlmClient"/> it returns a result and
/// touches NO database, so it runs under the replay adapter with zero credentials.
///
/// All three non-happy paths are handled here, never deferred:
///   - failed evidence validation -> that criterion is flagged EvidenceNotFound (no score);
///   - abstention / low confidence  -> criterion routed out of the composite -> human;
///   - LLM unavailable / unparseable -> criterion Errored, never a fabricated score.
/// Any incomplete criterion makes the composite null and the evaluation
/// NeedsHumanGrading (the default-safe state).
/// </summary>
public static class GradePipeline
{
    /// <summary>Replay/audit key for the per-criterion grading call.</summary>
    public const string Purpose = "grade-criterion";

    public static async Task<EvaluationResult> RunAsync(
        GradeRequest req, ILlmClient llm, CancellationToken ct = default)
    {
        // Redaction is unconditional and upstream of everything: this is the ONLY use of
        // the raw req.Turns. Both the prompt builder and the validator below see only
        // `redacted`, so un-redacted text can never reach the adapter (CLAUDE.md §4).
        var redacted = Redact.RedactTurns(req.Turns);
        var results = new List<CriterionResult>(req.Rubric.Criteria.Count);

        foreach (var criterion in req.Rubric.Criteria)
        {
            CriterionGrade grade;
            try
            {
                var prompt = GradingPrompt.Build(criterion, redacted);
                grade = await llm.CompleteStructuredAsync<CriterionGrade>(Purpose, prompt, ct);
            }
            catch (OperationCanceledException)
            {
                throw;   // shutdown / caller cancellation — don't mask it as a grade outcome
            }
            catch (Exception)
            {
                // LLM down or returned unparseable output for this criterion. Refuse to
                // invent a score; record it and let the evaluation route to a human (§3.5).
                results.Add(new CriterionResult(
                    criterion.Key, null, null, null, null, ValidationStatus.Errored));
                continue;
            }

            var status = Validate.ValidateEvidence(grade, redacted);
            results.Add(ToResult(criterion.Key, grade, status));
        }

        // The composite is computed in code, never by the LLM (§3.1). Null = incomplete.
        var composite = Aggregate.CompositeScore(results, req.Rubric.Criteria);
        var state = composite.HasValue ? EvaluationState.Graded : EvaluationState.NeedsHumanGrading;

        return new EvaluationResult(
            req.EvaluationId, req.TenantId, req.ArtifactId, req.Rubric.Id,
            state, composite, results, req.ModelId, GradingPrompt.Version);
    }

    /// <summary>Map a validated grade to a stored result. Only Valid keeps its score;
    /// rejected/uncertain outcomes keep their citation for the audit trail but drop the
    /// score so it can never feed the composite.</summary>
    private static CriterionResult ToResult(string key, CriterionGrade grade, ValidationStatus status)
        => status switch
        {
            ValidationStatus.Valid
                => new(key, grade.Score, grade.Confidence, grade.TurnId, grade.Evidence, status),
            ValidationStatus.LowConfidence
                => new(key, grade.Score, grade.Confidence, grade.TurnId, grade.Evidence, status),
            ValidationStatus.EvidenceNotFound
                => new(key, null, grade.Confidence, grade.TurnId, grade.Evidence, status),
            _ /* Abstained */
                => new(key, null, grade.Confidence, null, null, status),
        };
}
