using FluentAssertions;
using RubricGrader.Adapters;
using RubricGrader.Api;
using RubricGrader.Domain;
using RubricGrader.Grading;
using RubricGrader.Repository;
using Xunit;

namespace RubricGrader.Tests;

/// <summary>
/// The synchronous demo grade endpoint (POST /evaluations), exercised through its testable
/// core <see cref="EvaluationEndpoints.GradeInlineAsync"/>. It runs against the SAME committed
/// replay fixtures the live server uses (zero credentials), proving the canonical
/// audit-trace scenario flows end to end: two criteria are graded from verbatim quotes, the
/// fabricated-authentication ownership citation is caught (EvidenceNotFound, score dropped),
/// no composite is produced, and the result is persisted for the read/audit endpoints.
/// </summary>
public class GradeEndpointTests
{
    // The fixtures ship in the RubricGrader project tree (read from source at runtime, not
    // copied to bin), so locate them by walking up from the test assembly.
    private static string CommittedFixtureRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null &&
               !Directory.Exists(Path.Combine(dir.FullName, "RubricGrader", "Fixtures", "llm_replay")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "RubricGrader", "Fixtures", "llm_replay");
    }

    [Fact]
    public async Task PostEvaluations_GradesDemoArtifact_AndCatchesTheFabricatedOwnershipClaim()
    {
        var store = new InMemoryEvaluationStore();
        await store.SaveRubricAsync(GradeDemo.Rubric);
        var llm = new ReplayClaudeClient(CommittedFixtureRoot());

        var result = await EvaluationEndpoints.GradeInlineAsync(
            GradeDemo.Rubric, GradeDemo.TenantId, GradeDemo.ArtifactId,
            GradeDemo.Turns, GradeDemo.ModelId, llm, store);

        // Two criteria backed by verbatim quotes are graded...
        result.Results.Single(r => r.CriterionKey == "communication").ValidationStatus
            .Should().Be(ValidationStatus.Valid);
        result.Results.Single(r => r.CriterionKey == "communication").Score.Should().Be(4);
        result.Results.Single(r => r.CriterionKey == "technical_depth").ValidationStatus
            .Should().Be(ValidationStatus.Valid);
        result.Results.Single(r => r.CriterionKey == "technical_depth").Score.Should().Be(5);

        // ...the fabricated authentication claim is caught and its score dropped.
        var ownership = result.Results.Single(r => r.CriterionKey == "ownership");
        ownership.ValidationStatus.Should().Be(ValidationStatus.EvidenceNotFound);
        ownership.Score.Should().BeNull();

        // No composite is invented; the run routes to a human.
        result.CompositeScore.Should().BeNull();
        result.State.Should().Be(EvaluationState.NeedsHumanGrading);

        // The result is persisted (tenant-scoped) so GET /applications/{id} + /audit work.
        var persisted = await store.GetResultAsync(GradeDemo.TenantId, result.Id);
        persisted.Should().NotBeNull();
        persisted!.RubricVersionId.Should().Be(GradeDemo.RubricId);
    }
}
