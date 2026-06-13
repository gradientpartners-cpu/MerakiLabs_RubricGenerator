using RubricGrader.Grading;

namespace RubricGrader.Worker;

/// <summary>In-process async grade queue (CLAUDE.md §2): a bounded <c>Channel&lt;T&gt;</c>,
/// not a heavyweight job framework. The API enqueues; the BackgroundService drains.
/// Bounded so a flood of submissions applies backpressure instead of unbounded memory
/// growth. "Why not Hangfire": no durable retries / cross-process scheduling needed for
/// this slice — flagged as the upgrade point if durability is ever required.</summary>
public interface IGradeQueue
{
    ValueTask EnqueueAsync(GradeRequest request, CancellationToken ct = default);
    IAsyncEnumerable<GradeRequest> DequeueAllAsync(CancellationToken ct);
}
