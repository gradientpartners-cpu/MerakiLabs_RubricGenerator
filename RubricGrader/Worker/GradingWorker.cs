using RubricGrader.Adapters;
using RubricGrader.Grading;

namespace RubricGrader.Worker;

/// <summary>
/// The async grade worker (CLAUDE.md §2): a <see cref="BackgroundService"/> that drains
/// the <see cref="IGradeQueue"/>, runs the self-contained <see cref="GradePipeline"/>,
/// then hands the result to the sink as a SEPARATE step. The worker owns orchestration
/// and I/O; the pipeline owns the (pure, DB-free) grading logic.
/// </summary>
public sealed class GradingWorker : BackgroundService
{
    private readonly IGradeQueue _queue;
    private readonly ILlmClient _llm;
    private readonly IEvaluationResultSink _sink;
    private readonly ILogger<GradingWorker> _logger;

    public GradingWorker(
        IGradeQueue queue, ILlmClient llm, IEvaluationResultSink sink, ILogger<GradingWorker> logger)
    {
        _queue = queue;
        _llm = llm;
        _sink = sink;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                var result = await GradePipeline.RunAsync(job, _llm, stoppingToken);
                await _sink.SaveAsync(result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;   // shutting down
            }
            catch (Exception ex)
            {
                // One poisoned job must not kill the loop. The pipeline already absorbs
                // LLM failures into NeedsHumanGrading; reaching here means something
                // structural (e.g. a malformed rubric) — log and keep draining.
                _logger.LogError(ex, "Grading job {EvaluationId} failed unexpectedly.", job.EvaluationId);
            }
        }
    }
}
