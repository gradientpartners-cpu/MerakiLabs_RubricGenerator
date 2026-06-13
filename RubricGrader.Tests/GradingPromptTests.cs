using FluentAssertions;
using RubricGrader.Domain;
using RubricGrader.Grading;
using Xunit;

namespace RubricGrader.Tests;

/// <summary>
/// Tests for the per-criterion grading prompt. The prompt must be deterministic (so
/// replay keys are stable), must render anchors in score order, must surface every turn
/// id (the model can only cite what it sees), and must spell out the verbatim-evidence +
/// abstention rules that make the downstream validation honest (CLAUDE.md §3.1–§3.3).
/// </summary>
public class GradingPromptTests
{
    private static RubricCriterion Criterion()
        => new("communication", "Clear communication", 1.0,
            new Dictionary<int, string> { [3] = "adequate", [1] = "unclear", [5] = "exceptional" });

    private static IReadOnlyList<TranscriptTurn> Turns()
        => new[]
        {
            new TranscriptTurn("t1", "interviewer", "Tell me about a hard migration."),
            new TranscriptTurn("t2", "candidate", "I led the migration to a sharded cluster."),
        };

    [Fact]
    public void IsDeterministic()
        => GradingPrompt.Build(Criterion(), Turns())
            .Should().Be(GradingPrompt.Build(Criterion(), Turns()));

    [Fact]
    public void RendersAnchorsInScoreOrder()
    {
        var prompt = GradingPrompt.Build(Criterion(), Turns());

        prompt.IndexOf("1: unclear", StringComparison.Ordinal)
            .Should().BeLessThan(prompt.IndexOf("3: adequate", StringComparison.Ordinal));
        prompt.IndexOf("3: adequate", StringComparison.Ordinal)
            .Should().BeLessThan(prompt.IndexOf("5: exceptional", StringComparison.Ordinal));
    }

    [Fact]
    public void IncludesEveryTurnWithItsId()
    {
        var prompt = GradingPrompt.Build(Criterion(), Turns());

        prompt.Should().Contain("[t1] interviewer: Tell me about a hard migration.");
        prompt.Should().Contain("[t2] candidate: I led the migration to a sharded cluster.");
    }

    [Fact]
    public void StatesTheVerbatimAndAbstentionRules()
    {
        var prompt = GradingPrompt.Build(Criterion(), Turns());

        prompt.Should().Contain("verbatim substring");
        prompt.Should().Contain("ABSTAIN");
        prompt.Should().Contain("Never invent");
    }

    [Fact]
    public void RequestsTheExpectedJsonKeys()
    {
        var prompt = GradingPrompt.Build(Criterion(), Turns());

        foreach (var key in new[] { "criterionKey", "abstained", "score", "evidence", "turnId", "confidence" })
            prompt.Should().Contain($"\"{key}\"");
    }
}
