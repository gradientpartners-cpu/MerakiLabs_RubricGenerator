namespace RubricGrader.Fairness;

/// <summary>Per-group selection rate + four-fifths-rule outcome for one group.</summary>
public sealed record GroupRate(
    string Group,
    int Selected,
    int Total,
    double SelectionRate,
    double ImpactRatio,   // this group's rate / the highest group's rate
    bool Flagged          // ImpactRatio < 0.8
);

/// <summary>
/// Minimal four-fifths (80%) adverse-impact check (CLAUDE.md §4). Mechanism demo over
/// SEEDED SYNTHETIC data only — NOT a production audit (production would run this over
/// real aggregate outcomes across protected classes, with confidence intervals and
/// statistical-significance tests; that full report stays design-doc-only, §11). Pure.
/// </summary>
public static class AdverseImpact
{
    // The four-fifths threshold. EEOC's Uniform Guidelines define adverse impact as a
    // group selected at a rate LESS THAN 80% of the highest group's rate — so a group
    // sitting EXACTLY at 80% meets the rule and is NOT flagged. We make that boundary
    // decision deliberately (the rule is conventionally a strict "less than"), and use a
    // small tolerance so a ratio that is mathematically 0.8 but lands a hair under it
    // through floating-point division still counts as meeting the threshold.
    private const double Threshold = 0.80;
    private const double Epsilon = 1e-9;

    /// <summary>Given {group: (selected, total)}, compute each group's selection rate,
    /// its impact ratio against the top group, and an 80%-rule flag. Result is ordered by
    /// selection rate descending (top group first) then group name, for a readable report.</summary>
    public static IReadOnlyList<GroupRate> FourFifths(
        IReadOnlyDictionary<string, (int Selected, int Total)> selectionsByGroup)
    {
        if (selectionsByGroup.Count == 0)
            return Array.Empty<GroupRate>();

        // Selection rate per group. A group with no applicants has an undefined rate; we
        // treat it as 0 rather than dividing by zero (it cannot be the reference group).
        var rates = selectionsByGroup.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Total > 0 ? (double)kv.Value.Selected / kv.Value.Total : 0d);

        var maxRate = rates.Values.Max();

        return selectionsByGroup
            .Select(kv =>
            {
                var rate = rates[kv.Key];
                // No one selected anywhere -> no disparity to measure; ratio is 1.0, unflagged.
                var ratio = maxRate > 0 ? rate / maxRate : 1d;
                var flagged = ratio < Threshold - Epsilon;
                return new GroupRate(kv.Key, kv.Value.Selected, kv.Value.Total, rate, ratio, flagged);
            })
            .OrderByDescending(g => g.SelectionRate)
            .ThenBy(g => g.Group, StringComparer.Ordinal)
            .ToList();
    }
}
