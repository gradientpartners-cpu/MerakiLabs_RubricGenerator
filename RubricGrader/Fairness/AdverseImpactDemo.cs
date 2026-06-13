namespace RubricGrader.Fairness;

/// <summary>
/// SEEDED SYNTHETIC selection data used to demonstrate the four-fifths mechanism end to
/// end with zero credentials. These are made-up aggregate counts, NOT real candidates and
/// NOT a real audit — they exist so the check has something to bite on, and they are
/// deliberately rigged so one group falls below the 80% threshold (catching a disparity
/// demonstrates the mechanism far better than a clean pass). A production adverse-impact
/// report would compute over real aggregate outcomes; that stays design-doc-only (§11).
/// </summary>
public static class AdverseImpactDemo
{
    /// <summary>Group -> (selected, total). Group A is the reference (50% selection rate);
    /// Group C sits at 25% -> impact ratio 0.5, well under 0.8, so it trips the flag.</summary>
    public static readonly IReadOnlyDictionary<string, (int Selected, int Total)> Seed =
        new Dictionary<string, (int Selected, int Total)>
        {
            ["group_a"] = (50, 100),   // 0.50 — highest (reference)
            ["group_b"] = (42, 100),   // 0.42 — ratio 0.84, passes
            ["group_c"] = (25, 100),   // 0.25 — ratio 0.50, FLAGGED
        };
}
