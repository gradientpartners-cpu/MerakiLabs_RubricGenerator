using System.Text;
using RubricGrader.Domain;

namespace RubricGrader.Grading;

/// <summary>
/// Boundary validation = the anti-hallucination safeguard (CLAUDE.md §3.2), the
/// highest-signal function in the repo. A cited evidence span must be a verbatim
/// substring of the cited transcript turn, after normalisation. No fuzzy/semantic
/// matching — a near-miss is a CAUGHT hallucination, not a pass. Pure, no I/O.
/// </summary>
public static class Validate
{
    /// <summary>Whitespace-collapse + quote-glyph-unify + casefold, applied to both
    /// the cited evidence and the source turn before comparison. Deliberately narrow:
    /// it absorbs cosmetic transcription noise (smart quotes, re-wrapped whitespace,
    /// casing) but nothing semantic — paraphrase or invented words still fail.</summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                // collapse any run of whitespace into a single space, deferred so we
                // never emit a leading/trailing one
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(UnifyGlyph(ch));
        }

        return sb.ToString().ToLowerInvariant();
    }

    /// <summary>Map curly quotes/apostrophes and dash variants onto their ASCII form so
    /// "don't" and "don\u2019t" compare equal.</summary>
    private static char UnifyGlyph(char ch) => ch switch
    {
        '\u2018' or '\u2019' or '\u201B' or '\u2032' => '\'',         // ‘ ’ ‛ ′ -> '
        '\u201C' or '\u201D' or '\u201F' or '\u2033' => '"',          // “ ” ‟ ″ -> "
        '\u2013' or '\u2014' or '\u2212' => '-',                       // – — − -> -
        _ => ch
    };

    /// <summary>
    /// Classify a single criterion grade. Order (each routes uncertain/invalid cases
    /// away from a score): Abstained -> Abstained; Confidence==Low -> LowConfidence;
    /// evidence not a verbatim substring of the cited turn -> EvidenceNotFound; else Valid.
    /// </summary>
    public static ValidationStatus ValidateEvidence(CriterionGrade grade, IReadOnlyList<TranscriptTurn> turns)
    {
        if (grade.Abstained)
            return ValidationStatus.Abstained;

        if (grade.Confidence == Confidence.Low)
            return ValidationStatus.LowConfidence;

        // A non-abstaining, confident grade MUST carry a citation we can verify. A
        // missing span/turn is unverifiable, which we treat as evidence-not-found
        // rather than trusting an uncited score.
        if (string.IsNullOrWhiteSpace(grade.Evidence) || string.IsNullOrWhiteSpace(grade.TurnId))
            return ValidationStatus.EvidenceNotFound;

        var citedTurn = turns.FirstOrDefault(t => t.TurnId == grade.TurnId);
        if (citedTurn is null)
            return ValidationStatus.EvidenceNotFound;   // cited a turn that doesn't exist

        var haystack = Normalize(citedTurn.Text);
        var needle = Normalize(grade.Evidence);

        // Exact substring within the *cited* turn only — not anywhere in the transcript.
        return needle.Length > 0 && haystack.Contains(needle, StringComparison.Ordinal)
            ? ValidationStatus.Valid
            : ValidationStatus.EvidenceNotFound;
    }
}
