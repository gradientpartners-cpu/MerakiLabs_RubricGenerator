using RubricGrader.Domain;

namespace RubricGrader.Worker;

/// <summary>The seam between the (DB-free) grade pipeline and persistence. The worker
/// computes a result, then hands it here as a SEPARATE step (CLAUDE.md §5). Swapping the
/// in-memory store for an EF-backed one changes nothing in the pipeline or the worker.</summary>
public interface IEvaluationResultSink
{
    Task SaveAsync(EvaluationResult result, CancellationToken ct = default);
}
