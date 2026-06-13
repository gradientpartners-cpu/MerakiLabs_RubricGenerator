using RubricGrader.Domain;

namespace RubricGrader.Grading;

/// <summary>
/// Deterministic weighted composite (CLAUDE.md §3.1). The LLM NEVER computes this.
/// Pure function: same per-criterion results + weights -> same composite, always.
/// Only Valid criteria contribute; if a required criterion is not Valid the caller
/// routes the evaluation to NeedsHumanGrading rather than scoring a partial result.
/// </summary>
public static class Aggregate
{
    // Rubric weights are normalised to sum to 1.0 (Generation/Normalize). A composite is
    // only meaningful if that invariant holds; we surface a violation loudly rather than
    // emit a number computed over a malformed rubric.
    private const double WeightSumTolerance = 1e-6;

    /// <summary>Weighted sum of Valid per-criterion scores over their rubric weights.
    /// Returns null when the valid set can't form a complete composite (i.e. any rubric
    /// criterion lacks a Valid, scored result) — the caller then routes to a human.</summary>
    public static double? CompositeScore(
        IReadOnlyList<CriterionResult> results,
        IReadOnlyList<RubricCriterion> criteria)
    {
        if (criteria.Count == 0)
            return null;

        var weightSum = criteria.Sum(c => c.Weight);
        if (Math.Abs(weightSum - 1.0) > WeightSumTolerance)
            throw new ArgumentException(
                $"Rubric weights must sum to 1.0 but summed to {weightSum}.", nameof(criteria));

        // Index the Valid, scored results by criterion key. A criterion that abstained,
        // was low-confidence, or was a caught hallucination simply isn't here.
        var validScores = results
            .Where(r => r.ValidationStatus == ValidationStatus.Valid && r.Score.HasValue)
            .ToDictionary(r => r.CriterionKey, r => r.Score!.Value);

        double composite = 0.0;
        // Iterate rubric order (not results order) so the sum is reproducible regardless
        // of how the per-criterion results were collected.
        foreach (var criterion in criteria)
        {
            // THE composite-null rule, expressed once, here: if ANY rubric criterion
            // lacks a Valid score we return null rather than a partial number. A partial
            // composite would be a weighted sum over a DIFFERENT criteria set than the
            // rubric defines (weights would no longer sum to 1.0), so it would not be the
            // score the rubric describes. Null -> caller routes to NeedsHumanGrading.
            if (!validScores.TryGetValue(criterion.Key, out var score))
                return null;

            composite += criterion.Weight * score;
        }

        return composite;
    }
}
