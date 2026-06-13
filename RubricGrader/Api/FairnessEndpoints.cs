using RubricGrader.Fairness;

namespace RubricGrader.Api;

/// <summary>Fairness monitoring endpoint. Exposes the four-fifths adverse-impact check over
/// the seeded synthetic dataset so the mechanism is runnable from a clean clone with zero
/// credentials. This is a demonstration of the computation, not a production audit (see
/// <see cref="AdverseImpact"/> and DESIGN.md §11).</summary>
public static class FairnessEndpoints
{
    public static void MapFairnessEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/fairness/adverse-impact", () =>
        {
            var groups = AdverseImpact.FourFifths(AdverseImpactDemo.Seed);
            return Results.Ok(new
            {
                rule = "four-fifths (80%)",
                dataSource = "seeded synthetic data (demo of the mechanism, not a production audit)",
                anyFlagged = groups.Any(g => g.Flagged),
                groups,
            });
        });
    }
}
