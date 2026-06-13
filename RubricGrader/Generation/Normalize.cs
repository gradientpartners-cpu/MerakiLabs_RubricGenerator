using RubricGrader.Domain;

namespace RubricGrader.Generation;

/// <summary>
/// Weight normalisation for generated rubrics — the generator's "LLM doesn't own the
/// number" discipline (CLAUDE.md §9). The LLM proposes raw weights; code normalises
/// them to sum to 1.0 and builds the criteria. Pure.
/// </summary>
public static class Normalize
{
    /// <summary>Normalise RawWeight across drafts to sum to 1.0 and return RubricCriteria,
    /// preserving draft order. If the raw weights don't form a usable distribution
    /// (empty, zero, or any non-positive/non-finite weight), fall back to equal weights —
    /// we never trust the model's raw numbers, and a degenerate proposal must not yield a
    /// rubric whose weights silently fail to sum to 1.0 (which the grader's aggregator
    /// rejects).</summary>
    public static IReadOnlyList<RubricCriterion> NormalizeWeights(IReadOnlyList<DraftCriterion> drafts)
    {
        if (drafts.Count == 0)
            return Array.Empty<RubricCriterion>();

        var usable = drafts.All(d => double.IsFinite(d.RawWeight) && d.RawWeight > 0);
        var total = usable ? drafts.Sum(d => d.RawWeight) : 0d;

        // Equal split when the proposed weights can't be normalised meaningfully.
        var equal = 1.0 / drafts.Count;

        return drafts
            .Select(d => new RubricCriterion(
                d.Key,
                d.Label,
                usable ? d.RawWeight / total : equal,
                d.Anchors))
            .ToList();
    }
}
