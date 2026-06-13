using System.Text;

namespace RubricGrader.Generation;

/// <summary>
/// Versioned JD->rubric generation prompt (thin generator). One structured call
/// produces draft criteria with anchors + rationale; weights are normalised in code.
/// Bump <see cref="Version"/> on any change (recorded in the audit trail).
/// </summary>
public static class GenerationPrompt
{
    public const string Version = "generate-v1";

    /// <summary>Deterministic render: same JD -> same prompt string, so a replay fixture
    /// keyed on the prompt stays stable across runs.</summary>
    public static string Build(string jobDescription)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You design a hiring evaluation rubric from a job description.");
        sb.AppendLine("Produce 3-6 weighted, behaviorally-anchored criteria a human reviewer");
        sb.AppendLine("will APPROVE before any candidate is graded. The draft is reviewed, so");
        sb.AppendLine("propose clearly; a human owns the final rubric.");
        sb.AppendLine();

        sb.AppendLine("JOB DESCRIPTION:");
        sb.AppendLine(jobDescription);
        sb.AppendLine();

        sb.AppendLine("HARD RULES:");
        sb.AppendLine("- Each criterion needs a short key, a human label, a rawWeight (relative");
        sb.AppendLine("  importance; any positive number — it is re-normalised in code), anchors");
        sb.AppendLine("  for scores 1..5, and a one-line rationale tying it to the JD.");
        sb.AppendLine("- Criteria MUST be job-relevant. Do NOT propose anything keyed on age,");
        sb.AppendLine("  gender, race, nationality, disability, or other protected attributes,");
        sb.AppendLine("  nor proxies for them (e.g. 'culture fit', 'native speaker').");
        sb.AppendLine("- Anchors describe observable behaviour at each level, not personality.");
        sb.AppendLine();

        sb.AppendLine("Respond with ONLY this JSON object:");
        sb.AppendLine("{");
        sb.AppendLine("  \"criteria\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"key\": \"<short_snake_case_key>\",");
        sb.AppendLine("      \"label\": \"<human label>\",");
        sb.AppendLine("      \"rawWeight\": <positive number>,");
        sb.AppendLine("      \"anchors\": { \"1\": \"<weakest>\", \"3\": \"<mid>\", \"5\": \"<strongest>\" },");
        sb.AppendLine("      \"rationale\": \"<why this criterion, from the JD>\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.Append('}');

        return sb.ToString();
    }
}
