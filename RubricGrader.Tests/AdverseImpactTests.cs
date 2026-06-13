using FluentAssertions;
using RubricGrader.Fairness;
using Xunit;

namespace RubricGrader.Tests;

/// <summary>
/// The four-fifths (80%) adverse-impact check. We prove it (a) trips on the seeded
/// synthetic disparity and (b) treats the exact-80% boundary as a PASS, the deliberate
/// interpretation of EEOC's "less than four-fifths" wording (see <see cref="AdverseImpact"/>).
/// Pure computation — zero credentials, no I/O.
/// </summary>
public class AdverseImpactTests
{
    [Fact]
    public void Flags_TheGroupBelowFourFifths_OnSeededData()
    {
        var report = AdverseImpact.FourFifths(AdverseImpactDemo.Seed);

        // group_c (0.25 rate / 0.50 top = 0.50 ratio) is below 0.8 -> flagged.
        var c = report.Single(g => g.Group == "group_c");
        c.SelectionRate.Should().BeApproximately(0.25, 1e-9);
        c.ImpactRatio.Should().BeApproximately(0.50, 1e-9);
        c.Flagged.Should().BeTrue();

        // The reference group and the passing group are not flagged.
        report.Single(g => g.Group == "group_a").Flagged.Should().BeFalse();
        report.Single(g => g.Group == "group_b").Flagged.Should().BeFalse();

        // exactly one group trips on this seed
        report.Count(g => g.Flagged).Should().Be(1);
    }

    [Fact]
    public void Boundary_ExactlyEightyPercent_Passes()
    {
        // Reference group at 100% (10/10); the other at exactly 80% of it (8/10) -> ratio
        // 0.80. Per our deliberate "less than 0.8 flags" rule, exactly-0.8 is NOT flagged.
        var report = AdverseImpact.FourFifths(new Dictionary<string, (int, int)>
        {
            ["top"] = (10, 10),   // rate 1.0
            ["edge"] = (8, 10),   // rate 0.8 -> ratio exactly 0.80
        });

        var edge = report.Single(g => g.Group == "edge");
        edge.ImpactRatio.Should().BeApproximately(0.80, 1e-12);
        edge.Flagged.Should().BeFalse();
    }

    [Fact]
    public void JustBelowEightyPercent_Flags()
    {
        // One selection fewer than the boundary case (7/10 = 0.7 -> ratio 0.70) trips it,
        // confirming the boundary is the actual dividing line, not slack.
        var report = AdverseImpact.FourFifths(new Dictionary<string, (int, int)>
        {
            ["top"] = (10, 10),
            ["edge"] = (7, 10),
        });

        report.Single(g => g.Group == "edge").Flagged.Should().BeTrue();
    }
}
