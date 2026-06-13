using System.Text.Json;
using FluentAssertions;
using RubricGrader.Adapters;
using Xunit;

namespace RubricGrader.Tests;

/// <summary>
/// Tests for the zero-credential replay adapter (CLAUDE.md §4). It must read a recorded
/// response back into a typed result, key fixtures stably by prompt, and fail loud (not
/// silently) when a fixture is missing or the wrong shape.
/// </summary>
public class ReplayClaudeClientTests : IDisposable
{
    private record Judgment(string CriterionKey, int Score, string Evidence);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "replay-" + Guid.NewGuid().ToString("N"));

    private void WriteFixture(string purpose, string prompt, object response)
    {
        var dir = Path.Combine(_root, purpose);
        Directory.CreateDirectory(dir);
        var envelope = new { purpose, prompt, response };
        File.WriteAllText(
            Path.Combine(dir, ReplayClaudeClient.KeyFor(prompt) + ".json"),
            JsonSerializer.Serialize(envelope));
    }

    [Fact]
    public async Task ReplaysRecordedResponse_IntoTypedResult()
    {
        WriteFixture("grade-criterion", "PROMPT-A",
            new { criterionKey = "communication", score = 4, evidence = "led the migration" });
        var client = new ReplayClaudeClient(_root);

        var result = await client.CompleteStructuredAsync<Judgment>("grade-criterion", "PROMPT-A");

        result.CriterionKey.Should().Be("communication");
        result.Score.Should().Be(4);
        result.Evidence.Should().Be("led the migration");
    }

    [Fact]
    public async Task MissingFixture_ThrowsHelpfully()
    {
        var client = new ReplayClaudeClient(_root);

        var act = () => client.CompleteStructuredAsync<Judgment>("grade-criterion", "NEVER-RECORDED");

        (await act.Should().ThrowAsync<ReplayFixtureMissingException>())
            .WithMessage("*grade-criterion*");
    }

    [Fact]
    public async Task DifferentPrompts_KeyToDifferentFixtures()
    {
        WriteFixture("grade-criterion", "PROMPT-A", new { criterionKey = "a", score = 1, evidence = "x" });
        WriteFixture("grade-criterion", "PROMPT-B", new { criterionKey = "b", score = 5, evidence = "y" });
        var client = new ReplayClaudeClient(_root);

        (await client.CompleteStructuredAsync<Judgment>("grade-criterion", "PROMPT-A")).Score.Should().Be(1);
        (await client.CompleteStructuredAsync<Judgment>("grade-criterion", "PROMPT-B")).Score.Should().Be(5);
    }

    [Fact]
    public void KeyFor_IsDeterministic()
        => ReplayClaudeClient.KeyFor("same prompt").Should().Be(ReplayClaudeClient.KeyFor("same prompt"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
