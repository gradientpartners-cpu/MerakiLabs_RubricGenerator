using System.Text;
using RubricGrader.Domain;

namespace RubricGrader.Grading;

/// <summary>
/// Versioned per-criterion grading prompt. The model judges ONE criterion at a time
/// against that criterion's anchors + the redacted transcript (CLAUDE.md §3.1).
/// Bump <see cref="Version"/> on any text change — it is recorded in the audit trail
/// and reproducibility depends on it.
/// </summary>
public static class GradingPrompt
{
    public const string Version = "grade-v1";

    /// <summary>Deterministic render: same criterion + turns -> same prompt string, so a
    /// replay fixture keyed on the prompt stays stable across runs.</summary>
    public static string Build(RubricCriterion criterion, IReadOnlyList<TranscriptTurn> redactedTurns)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are grading ONE rubric criterion of a candidate interview transcript.");
        sb.AppendLine("Judge only the criterion below. Do not score any other dimension.");
        sb.AppendLine();

        sb.AppendLine($"CRITERION: {criterion.Key} — {criterion.Label}");
        sb.AppendLine("SCORE ANCHORS (1 = weakest, 5 = strongest):");
        foreach (var (level, descriptor) in criterion.Anchors.OrderBy(a => a.Key))
            sb.AppendLine($"  {level}: {descriptor}");
        sb.AppendLine();

        sb.AppendLine("TRANSCRIPT (already redacted; cite by turn id):");
        foreach (var turn in redactedTurns)
            sb.AppendLine($"  [{turn.TurnId}] {turn.Speaker}: {turn.Text}");
        sb.AppendLine();

        sb.AppendLine("HARD RULES:");
        sb.AppendLine("- Evidence MUST be a verbatim substring copied from exactly ONE transcript turn.");
        sb.AppendLine("- Do NOT paraphrase, summarise, or combine turns. Copy the words exactly.");
        sb.AppendLine("- If the transcript lacks evidence to judge this criterion, ABSTAIN.");
        sb.AppendLine("- Never invent a quote, a turn id, or a score. Abstaining is expected and safe.");
        sb.AppendLine("- 'confidence' is your own triage signal; use 'low' when unsure.");
        sb.AppendLine();

        sb.AppendLine("Respond with ONLY this JSON object:");
        sb.AppendLine("{");
        sb.AppendLine($"  \"criterionKey\": \"{criterion.Key}\",");
        sb.AppendLine("  \"abstained\": <true|false>,");
        sb.AppendLine("  \"score\": <integer 1-5, or null if abstained>,");
        sb.AppendLine("  \"evidence\": \"<verbatim substring of the cited turn, or null if abstained>\",");
        sb.AppendLine("  \"turnId\": \"<the cited turn id, or null if abstained>\",");
        sb.AppendLine("  \"confidence\": \"<high|medium|low>\"");
        sb.Append('}');

        return sb.ToString();
    }
}
