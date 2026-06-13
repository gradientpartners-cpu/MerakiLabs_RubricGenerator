using System.Text.RegularExpressions;
using RubricGrader.Domain;

namespace RubricGrader.Grading;

/// <summary>
/// PII / name-blind redaction BEFORE the artifact reaches the LLM (CLAUDE.md §4):
/// cheap and directly anti-bias — the model never sees names/contact details
/// correlated with protected class. Pure. Must preserve turn ids so evidence
/// validation still works on the redacted text.
///
/// Scope is deliberate and honest: unambiguous PII (email / phone / URL) plus
/// self-introduction name patterns. Full NER-based name-blinding is design-doc-only
/// (CLAUDE.md §4) — over-eager capitalised-token masking would shred technical nouns
/// ("Postgres", "Azure") and weaken the evidence-validation surface.
/// </summary>
public static partial class Redact
{
    // Order matters: email before URL (so an address isn't half-eaten by the URL rule).
    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"\b(?:https?://|www\.)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Url();

    // Typical 10-digit phone with optional country code / separators. Requires phone-shaped
    // grouping so it doesn't swallow plain large numbers ("1000000 users").
    [GeneratedRegex(@"(?:\+\d{1,3}[\s.-]?)?\(?\d{3}\)?[\s.-]?\d{3}[\s.-]?\d{4}\b")]
    private static partial Regex Phone();

    // Self-introduction only — masks the captured name, keeps the lead-in intact.
    [GeneratedRegex(@"\b(?:my name is|i am|i'm|this is)\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)",
        RegexOptions.IgnoreCase)]
    private static partial Regex NameIntro();

    /// <summary>Return turns with candidate name / contact / demographic-revealing
    /// tokens masked (names, emails, phones, addresses).</summary>
    public static IReadOnlyList<TranscriptTurn> RedactTurns(IReadOnlyList<TranscriptTurn> turns)
        => turns.Select(t => t with { Text = RedactText(t.Text) }).ToList();

    private static string RedactText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        text = Email().Replace(text, "[EMAIL]");
        text = Url().Replace(text, "[URL]");
        text = Phone().Replace(text, "[PHONE]");
        // Replace only the captured name group, preserving the surrounding lead-in.
        text = NameIntro().Replace(text, m =>
            m.Value[..(m.Groups[1].Index - m.Index)] + "[NAME]");

        return text;
    }
}
