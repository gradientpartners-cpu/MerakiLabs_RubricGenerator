using System.Text.Json;
using FluentAssertions;
using RubricGrader.Adapters;
using RubricGrader.Domain;
using RubricGrader.Generation;
using Xunit;

namespace RubricGrader.Tests;

/// <summary>
/// The thin generator's invariants (CLAUDE.md §9): weights are normalised in code (the
/// LLM never owns the number), unusable proposals are rejected, and the generator
/// degrades to a fixed baseline rubric — always a Draft — rather than failing or
/// fabricating one. The end-to-end tests run through the REAL replay adapter with zero
/// credentials.
/// </summary>
public class GeneratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gen-" + Guid.NewGuid().ToString("N"));

    private static DraftCriterion Draft(string key, double raw)
        => new(key, key + "-label", raw, new Dictionary<int, string> { [1] = "weak", [5] = "strong" }, "because");

    private ReplayClaudeClient Llm => new(_root);

    private void SeedGeneration(string jd, object response)
    {
        var prompt = GenerationPrompt.Build(jd);
        var dir = Path.Combine(_root, RubricGenerator.Purpose);
        Directory.CreateDirectory(dir);
        var envelope = new { purpose = RubricGenerator.Purpose, prompt, response };
        File.WriteAllText(
            Path.Combine(dir, ReplayClaudeClient.KeyFor(prompt) + ".json"),
            JsonSerializer.Serialize(envelope));
    }

    // ---- NormalizeWeights (pure) -------------------------------------------------

    [Fact]
    public void NormalizeWeights_RescalesToSumToOne_PreservingOrder()
    {
        var result = Normalize.NormalizeWeights(new[] { Draft("a", 3), Draft("b", 1) });

        result.Select(c => c.Key).Should().Equal("a", "b");
        result.Sum(c => c.Weight).Should().BeApproximately(1.0, 1e-9);
        result[0].Weight.Should().BeApproximately(0.75, 1e-9);
        result[1].Weight.Should().BeApproximately(0.25, 1e-9);
    }

    [Theory]
    [InlineData(0.0, 0.0)]      // all-zero total
    [InlineData(-1.0, -2.0)]    // negative weights
    public void NormalizeWeights_FallsBackToEqualSplit_OnDegenerateWeights(double w1, double w2)
    {
        var result = Normalize.NormalizeWeights(new[] { Draft("a", w1), Draft("b", w2) });

        result.Sum(c => c.Weight).Should().BeApproximately(1.0, 1e-9);
        result.Should().OnlyContain(c => Math.Abs(c.Weight - 0.5) < 1e-9);
    }

    // ---- GenerationValidation ----------------------------------------------------

    [Fact]
    public void Validation_Rejects_EmptyCriteria()
    {
        var act = () => GenerationValidation.EnsureUsable(Array.Empty<DraftCriterion>());
        act.Should().Throw<GenerationValidation.UnusableDraftException>();
    }

    [Fact]
    public void Validation_Rejects_DuplicateKeys()
    {
        var act = () => GenerationValidation.EnsureUsable(new[] { Draft("a", 1), Draft("a", 1) });
        act.Should().Throw<GenerationValidation.UnusableDraftException>();
    }

    [Fact]
    public void Validation_Rejects_MissingAnchors()
    {
        var bad = new DraftCriterion("a", "A", 1, new Dictionary<int, string>(), "r");
        var act = () => GenerationValidation.EnsureUsable(new[] { bad });
        act.Should().Throw<GenerationValidation.UnusableDraftException>();
    }

    // ---- fairness denylist: flags, never drops -----------------------------------

    [Fact]
    public void FlagProtectedAttributes_FlagsMatch_WithoutDroppingCriterion()
    {
        var criteria = new[]
        {
            new RubricCriterion("tech", "Technical depth", 0.5, new Dictionary<int, string> { [5] = "deep" }),
            new RubricCriterion("vibe", "Culture fit and age", 0.5, new Dictionary<int, string> { [5] = "good" }),
        };

        var flags = GenerationValidation.FlagProtectedAttributes(criteria);

        // The offending criterion is flagged for the reviewer...
        flags.Should().ContainSingle(f => f.CriterionKey == "vibe");
        // ...but nothing is removed — the reviewer adjudicates, the denylist does not.
        criteria.Should().HaveCount(2);
    }

    [Fact]
    public async Task Generate_SurfacesFairnessFlag_ButKeepsTheCriterion()
    {
        const string jd = "role where the model proposes an age-based criterion";
        SeedGeneration(jd, new
        {
            criteria = new[]
            {
                new { key = "youthful", label = "Youthful energy (age)", rawWeight = 1.0,
                      anchors = new Dictionary<string, string> { ["1"] = "no", ["5"] = "yes" },
                      rationale = "should be flagged" },
            },
        });

        var result = await RubricGenerator.GenerateAsync("tenant-1", "rub-new", jd, Llm);

        result.FairnessFlags.Should().ContainSingle(f => f.CriterionKey == "youthful");
        result.Rubric.Criteria.Should().ContainSingle(c => c.Key == "youthful");   // not dropped
    }

    // ---- end-to-end through the replay adapter -----------------------------------

    [Fact]
    public async Task Generate_ProducesApprovableDraft_WithWeightsNormalisedInCode()
    {
        const string jd = "Senior backend engineer who owns services end to end.";
        SeedGeneration(jd, new
        {
            criteria = new[]
            {
                new { key = "tech", label = "Technical depth", rawWeight = 6.0,
                      anchors = new Dictionary<string, string> { ["1"] = "shallow", ["5"] = "deep" },
                      rationale = "core of the role" },
                new { key = "comm", label = "Communication", rawWeight = 2.0,
                      anchors = new Dictionary<string, string> { ["1"] = "unclear", ["5"] = "clear" },
                      rationale = "cross-team work" },
            },
        });

        var result = await RubricGenerator.GenerateAsync("tenant-1", "rub-new", jd, Llm);
        var rubric = result.Rubric;

        rubric.Status.Should().Be(RubricStatus.Draft);          // human approval still required
        rubric.TenantId.Should().Be("tenant-1");
        rubric.Criteria.Select(c => c.Key).Should().Equal("tech", "comm");
        rubric.Criteria.Sum(c => c.Weight).Should().BeApproximately(1.0, 1e-9);   // normalised in code
        rubric.Criteria.Single(c => c.Key == "tech").Weight.Should().BeApproximately(0.75, 1e-9);
        result.FairnessFlags.Should().BeEmpty();                // clean job-relevant criteria
    }

    [Fact]
    public async Task Generate_DegradesToFixedBaseline_WhenLlmFixtureMissing()
    {
        // No fixture seeded: the replay adapter throws, and the generator must degrade to
        // the fixed baseline rubric rather than fail or fabricate.
        var rubric = (await RubricGenerator.GenerateAsync("tenant-1", "rub-new", "anything", Llm)).Rubric;

        rubric.Status.Should().Be(RubricStatus.Draft);          // still gated by human approval
        rubric.Criteria.Select(c => c.Key)
            .Should().Equal(FallbackRubric.Criteria.Select(c => c.Key));
        rubric.Criteria.Sum(c => c.Weight).Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public async Task Generate_DegradesToFixedBaseline_WhenProposalIsUnusable()
    {
        const string jd = "role with an empty rubric proposal";
        SeedGeneration(jd, new { criteria = Array.Empty<object>() });   // valid shape, zero criteria

        var rubric = (await RubricGenerator.GenerateAsync("tenant-1", "rub-new", jd, Llm)).Rubric;

        rubric.Criteria.Select(c => c.Key)
            .Should().Equal(FallbackRubric.Criteria.Select(c => c.Key));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
