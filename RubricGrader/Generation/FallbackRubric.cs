using RubricGrader.Domain;

namespace RubricGrader.Generation;

/// <summary>
/// The fixed baseline the generator degrades to when the LLM is unavailable or returns an
/// unusable proposal (CLAUDE.md §9 GATE). A generic, role-agnostic competency rubric: it
/// is intentionally bland and job-neutral so it can stand in for any screen without
/// asserting role-specific judgement. It is emitted as a Draft like any generated rubric,
/// so a human still reviews and approves it before the grader uses it.
/// </summary>
public static class FallbackRubric
{
    public static readonly IReadOnlyList<DraftCriterion> Criteria = new[]
    {
        new DraftCriterion(
            "communication", "Clarity of communication", 1.0,
            new Dictionary<int, string>
            {
                [1] = "Explanations are unclear or hard to follow.",
                [3] = "Communicates adequately with some prompting.",
                [5] = "Explains complex ideas clearly and concisely.",
            },
            "Baseline criterion (LLM fallback): communication applies to virtually any role."),

        new DraftCriterion(
            "problem_solving", "Problem solving", 1.0,
            new Dictionary<int, string>
            {
                [1] = "Struggles to break down problems or reason about trade-offs.",
                [3] = "Solves routine problems with a workable approach.",
                [5] = "Decomposes hard problems and reasons clearly about trade-offs.",
            },
            "Baseline criterion (LLM fallback): structured problem solving is broadly relevant."),

        new DraftCriterion(
            "ownership", "Ownership & accountability", 1.0,
            new Dictionary<int, string>
            {
                [1] = "Deflects responsibility; little evidence of follow-through.",
                [3] = "Takes ownership of assigned work.",
                [5] = "Drives work end to end and owns outcomes, including failures.",
            },
            "Baseline criterion (LLM fallback): ownership is a general signal of effectiveness."),
    };
}
