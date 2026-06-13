using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RubricGrader.Adapters;
using RubricGrader.Domain;
using RubricGrader.Grading;
using RubricGrader.Worker;
using Xunit;

namespace RubricGrader.Tests;

/// <summary>
/// Tests the worker wiring: a job enqueued on the Channel is drained by the
/// BackgroundService, graded by the pipeline, and handed to the sink as a separate
/// step — all with zero database.
/// </summary>
public class GradingWorkerTests
{
    /// <summary>Returns a fixed valid grade for any prompt — keeps the worker test about
    /// orchestration, not fixtures (the pipeline tests cover the replay adapter).</summary>
    private sealed class FakeLlm : ILlmClient
    {
        private readonly CriterionGrade _grade;
        public FakeLlm(CriterionGrade grade) => _grade = grade;
        public Task<T> CompleteStructuredAsync<T>(string purpose, string prompt, CancellationToken ct = default)
            => Task.FromResult((T)(object)_grade);
    }

    /// <summary>Sink that completes a task when a result is saved — lets the test await
    /// the worker instead of polling.</summary>
    private sealed class SignalingSink : IEvaluationResultSink
    {
        private readonly TaskCompletionSource<EvaluationResult> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<EvaluationResult> Saved => _tcs.Task;
        public Task SaveAsync(EvaluationResult result, CancellationToken ct = default)
        {
            _tcs.TrySetResult(result);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task EnqueuedJob_IsGraded_AndHandedToSink()
    {
        var queue = new ChannelGradeQueue();
        var sink = new SignalingSink();
        var grade = new CriterionGrade("comm", false, 5, "led the migration", "t1", Confidence.High);
        var worker = new GradingWorker(queue, new FakeLlm(grade), sink, NullLogger<GradingWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        var criterion = new RubricCriterion("comm", "comm", 1.0, new Dictionary<int, string>());
        var rubric = new RubricVersion("rub-1", "tenant-1", 1, RubricStatus.Approved, new[] { criterion });
        var turns = new[] { new TranscriptTurn("t1", "candidate", "I led the migration.") };
        await queue.EnqueueAsync(new GradeRequest("eval-1", "tenant-1", "art-1", rubric, turns, "test"));

        var result = await sink.Saved.WaitAsync(TimeSpan.FromSeconds(5));

        result.Id.Should().Be("eval-1");
        result.State.Should().Be(EvaluationState.Graded);
        result.CompositeScore.Should().BeApproximately(5.0, 1e-9);

        await worker.StopAsync(CancellationToken.None);
    }
}
