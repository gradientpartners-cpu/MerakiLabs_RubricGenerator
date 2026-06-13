using FluentAssertions;
using RubricGrader.Domain;
using RubricGrader.Grading;
using Xunit;

namespace RubricGrader.Tests;

/// <summary>
/// Tests for the deterministic composite (CLAUDE.md §3.1). The composite is computed in
/// code, never by the LLM — so it must be reproducible, must refuse to score an
/// incomplete set, and must reject a malformed (non-normalised) rubric.
/// </summary>
public class AggregateTests
{
    private static RubricCriterion Crit(string key, double weight)
        => new(key, key, weight, new Dictionary<int, string>());

    private static CriterionResult Valid(string key, int score)
        => new(key, score, Confidence.High, $"{key}-turn", "span", ValidationStatus.Valid);

    private static CriterionResult NotValid(string key, ValidationStatus status)
        => new(key, null, null, null, null, status);

    [Fact]
    public void WeightedSum_MatchesHandComputed()
    {
        var criteria = new[] { Crit("a", 0.5), Crit("b", 0.3), Crit("c", 0.2) };
        var results = new[] { Valid("a", 4), Valid("b", 5), Valid("c", 2) };

        // 0.5*4 + 0.3*5 + 0.2*2 = 2.0 + 1.5 + 0.4 = 3.9
        Aggregate.CompositeScore(results, criteria).Should().BeApproximately(3.9, 1e-9);
    }

    [Fact]
    public void OrderOfResults_DoesNotChangeComposite()
    {
        var criteria = new[] { Crit("a", 0.5), Crit("b", 0.3), Crit("c", 0.2) };
        var forward = new[] { Valid("a", 4), Valid("b", 5), Valid("c", 2) };
        var shuffled = new[] { Valid("c", 2), Valid("a", 4), Valid("b", 5) };

        Aggregate.CompositeScore(shuffled, criteria)
            .Should().Be(Aggregate.CompositeScore(forward, criteria));
    }

    [Theory]
    [InlineData(ValidationStatus.Abstained)]
    [InlineData(ValidationStatus.LowConfidence)]
    [InlineData(ValidationStatus.EvidenceNotFound)]
    public void AnyNonValidRequiredCriterion_YieldsNullComposite(ValidationStatus status)
    {
        var criteria = new[] { Crit("a", 0.5), Crit("b", 0.5) };
        var results = new[] { Valid("a", 4), NotValid("b", status) };

        Aggregate.CompositeScore(results, criteria).Should().BeNull();
    }

    [Fact]
    public void MissingCriterionResult_YieldsNullComposite()
    {
        var criteria = new[] { Crit("a", 0.5), Crit("b", 0.5) };
        var results = new[] { Valid("a", 4) };   // nothing for "b" at all

        Aggregate.CompositeScore(results, criteria).Should().BeNull();
    }

    [Fact]
    public void EmptyRubric_YieldsNull()
    {
        Aggregate.CompositeScore(Array.Empty<CriterionResult>(), Array.Empty<RubricCriterion>())
            .Should().BeNull();
    }

    [Fact]
    public void WeightsNotSummingToOne_Throws()
    {
        var criteria = new[] { Crit("a", 0.5), Crit("b", 0.3) };   // sums to 0.8
        var results = new[] { Valid("a", 4), Valid("b", 5) };

        var act = () => Aggregate.CompositeScore(results, criteria);
        act.Should().Throw<ArgumentException>().WithMessage("*sum to 1.0*");
    }

    [Fact]
    public void SameInputs_AreDeterministic()
    {
        var criteria = new[] { Crit("a", 0.34), Crit("b", 0.33), Crit("c", 0.33) };
        var results = new[] { Valid("a", 3), Valid("b", 4), Valid("c", 5) };

        var first = Aggregate.CompositeScore(results, criteria);
        var second = Aggregate.CompositeScore(results, criteria);

        second.Should().Be(first);
    }
}
