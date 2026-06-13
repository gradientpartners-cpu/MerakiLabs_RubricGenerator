using RubricGrader.Domain;

namespace RubricGrader.Generation;

/// <summary>Boundary checks on the model's proposed criteria, BEFORE weight
/// normalisation. A proposal that fails here is unusable; the generator catches the throw
/// and degrades to the fixed baseline rubric (CLAUDE.md §9), never a fabricated one.</summary>
public static class GenerationValidation
{
    public sealed class UnusableDraftException : Exception
    {
        public UnusableDraftException(string message) : base(message) { }
    }

    /// <summary>Throws <see cref="UnusableDraftException"/> if the draft set can't become a
    /// valid rubric: it must be non-empty, every criterion needs a key + label + at least
    /// one anchor, and keys must be unique (duplicate keys would collide in the grader's
    /// per-criterion map).</summary>
    public static void EnsureUsable(IReadOnlyList<DraftCriterion> drafts)
    {
        if (drafts.Count == 0)
            throw new UnusableDraftException("Generated rubric has no criteria.");

        foreach (var d in drafts)
        {
            if (string.IsNullOrWhiteSpace(d.Key))
                throw new UnusableDraftException("A generated criterion is missing its key.");
            if (string.IsNullOrWhiteSpace(d.Label))
                throw new UnusableDraftException($"Criterion '{d.Key}' is missing its label.");
            if (d.Anchors is null || d.Anchors.Count == 0)
                throw new UnusableDraftException($"Criterion '{d.Key}' has no score anchors.");
        }

        var keys = drafts.Select(d => d.Key).ToList();
        if (keys.Distinct().Count() != keys.Count)
            throw new UnusableDraftException("Generated criteria contain duplicate keys.");
    }

    // Obvious protected-attribute terms (US Title VII / EEOC categories + common proxies).
    // Deliberately a blunt substring denylist, NOT semantic proxy detection — it exists to
    // raise a cheap, reviewable signal, not to judge. False positives are acceptable
    // because the reviewer adjudicates; it never auto-drops a criterion.
    private static readonly string[] ProtectedTerms =
    {
        "age", "gender", "sex", "race", "ethnic", "religio", "nationality", "national origin",
        "marital", "family status", "pregnan", "disab", "orientation", "veteran", "citizenship",
        "native speaker", "culture fit", "culture-fit",
    };

    /// <summary>
    /// Scan generated criteria (key + label) for obvious protected-attribute terms and
    /// return a flag per match. This is a NON-blocking fairness signal: it never throws and
    /// never drops a criterion — the matches are handed to the human reviewer, who decides.
    /// One code-level layer on top of the prompt and the approval gate (DESIGN.md §8).
    /// </summary>
    public static IReadOnlyList<FairnessFlag> FlagProtectedAttributes(IReadOnlyList<RubricCriterion> criteria)
    {
        var flags = new List<FairnessFlag>();
        foreach (var c in criteria)
        {
            var haystack = $"{c.Key} {c.Label}".ToLowerInvariant();
            foreach (var term in ProtectedTerms)
            {
                if (haystack.Contains(term, StringComparison.Ordinal))
                {
                    flags.Add(new FairnessFlag(c.Key, term));
                    break; // one flag per criterion is enough to route it to review
                }
            }
        }
        return flags;
    }
}
