using RubricGrader.Adapters;
using RubricGrader.Domain;

namespace RubricGrader.Generation;

/// <summary>What the single generation LLM call returns — a set of proposed draft
/// criteria. Deserialized from the adapter's structured response.</summary>
public sealed record GeneratedRubric(IReadOnlyList<DraftCriterion> Criteria);

/// <summary>The generator's output: the Draft rubric (which now carries its own fairness
/// flags so they persist and resurface at approval) plus the same flags hoisted here for
/// the immediate /jd response (CLAUDE.md §9, DESIGN.md §8).</summary>
public sealed record GenerationResult(
    RubricVersion Rubric,
    IReadOnlyList<FairnessFlag> FairnessFlags);

/// <summary>
/// The thin JD->rubric generator (CLAUDE.md §9): one structured LLM call -> validate ->
/// normalise weights in code -> a <b>Draft</b> rubric a human must approve before the
/// grader will touch it. The generator is deliberately shallow; its low stakes come from
/// that approval gate, which is why it does not carry the grader's depth.
///
/// GATE (CLAUDE.md §9): if the LLM is unavailable or returns an unusable proposal, the
/// generator degrades to a fixed baseline rubric rather than failing or fabricating a
/// bespoke one. The fallback is still emitted as a Draft, so the human-approval gate
/// still applies — degradation lowers rubric <i>quality</i>, never the safety boundary.
/// </summary>
public static class RubricGenerator
{
    public const string Purpose = "generate-rubric";

    public static async Task<GenerationResult> GenerateAsync(
        string tenantId, string rubricId, string jobDescription,
        ILlmClient llm, CancellationToken ct = default)
    {
        IReadOnlyList<DraftCriterion> drafts;
        try
        {
            var prompt = GenerationPrompt.Build(jobDescription);
            var response = await llm.CompleteStructuredAsync<GeneratedRubric>(Purpose, prompt, ct);
            drafts = response?.Criteria ?? Array.Empty<DraftCriterion>();
            GenerationValidation.EnsureUsable(drafts);
        }
        catch (OperationCanceledException)
        {
            throw; // cancellation is not a degradation path
        }
        catch (Exception)
        {
            // LLM unavailable / unparseable / unusable proposal -> fixed baseline, never
            // a fabricated bespoke rubric. Still a Draft: the human still approves it.
            drafts = FallbackRubric.Criteria;
        }

        var criteria = Normalize.NormalizeWeights(drafts);

        // Non-blocking fairness signal for the reviewer — flags, never drops (DESIGN.md §8).
        // Stored ON the Draft so it survives persistence and resurfaces at approval time;
        // the human gate must not approve blind.
        var flags = GenerationValidation.FlagProtectedAttributes(criteria);
        var rubric = new RubricVersion(
            rubricId, tenantId, 1, RubricStatus.Draft, criteria, FairnessFlags: flags);

        return new GenerationResult(rubric, flags);
    }
}
