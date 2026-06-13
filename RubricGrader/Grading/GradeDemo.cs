using RubricGrader.Domain;

namespace RubricGrader.Grading;

/// <summary>
/// SEEDED canonical grade scenario used by the synchronous demo endpoint (POST /evaluations)
/// so the grader can be exercised end-to-end with ZERO credentials. It is the exact case
/// rendered in <c>examples/audit-trace-example.json</c>/<c>.md</c>: a Backend Engineer Screen
/// rubric (v3, Approved) and a six-turn transcript where two criteria are backed by verbatim
/// quotes and the third (ownership) cites a span that does NOT appear in its turn — so the
/// pipeline rejects it (EvidenceNotFound), drops the score, and routes to a human with a null
/// composite. This is demo seed data, not a real candidate or a real rubric; it exists so the
/// "caught hallucination" path is runnable from a clean clone. The committed replay fixtures
/// under <c>Fixtures/llm_replay/grade-criterion/</c> are keyed on the prompts built from this
/// rubric + (redacted) transcript, so they only stay valid as long as both match.
/// </summary>
public static class GradeDemo
{
    public const string RubricId = "rub-backend-screen-v3";
    public const string TenantId = "tenant-acme";
    public const string ArtifactId = "artifact-7731";

    /// <summary>Model label recorded on the persisted result so the audit trail reads
    /// honestly (a replayed Claude fixture, not a live call).</summary>
    public const string ModelId = "replay (claude-sonnet-4-6 fixtures)";

    /// <summary>The frozen, Approved rubric the grader consumes. Anchors are fixed here
    /// because the replay fixture keys depend on the exact prompt text (criterion + anchors).</summary>
    public static readonly RubricVersion Rubric = new(
        Id: RubricId,
        TenantId: TenantId,
        Version: 3,
        Status: RubricStatus.Approved,
        Criteria: new[]
        {
            new RubricCriterion("communication", "Clarity of communication", 0.3,
                new Dictionary<int, string>
                {
                    [1] = "Rambling or vague; hard to follow.",
                    [3] = "Generally clear with some unfocused passages.",
                    [5] = "Crisp, well-structured, and easy to follow.",
                }),
            new RubricCriterion("technical_depth", "Technical depth", 0.4,
                new Dictionary<int, string>
                {
                    [1] = "Surface-level; no concrete detail.",
                    [3] = "Sound understanding with some gaps.",
                    [5] = "Deep, specific, and quantified.",
                }),
            new RubricCriterion("ownership", "Ownership & accountability", 0.3,
                new Dictionary<int, string>
                {
                    [1] = "Deflects responsibility.",
                    [3] = "Shares ownership when prompted.",
                    [5] = "Drives outcomes and owns the follow-through.",
                }),
        });

    /// <summary>The interview transcript of record (mirrors the audit-trace example). Note
    /// t4 is about a Postgres failover, NOT authentication — the ownership citation in the
    /// fixture quotes an authentication rebuild that is absent here, so it fails validation.</summary>
    public static readonly IReadOnlyList<TranscriptTurn> Turns = new[]
    {
        new TranscriptTurn("t1", "interviewer", "Walk me through a system you owned end to end."),
        new TranscriptTurn("t2", "candidate", "I owned our billing pipeline for two years, from ingestion to invoicing."),
        new TranscriptTurn("t3", "interviewer", "How did you handle a major incident?"),
        new TranscriptTurn("t4", "candidate", "During a Postgres failover I coordinated the rollback and wrote the postmortem myself."),
        new TranscriptTurn("t5", "interviewer", "Tell me about scaling."),
        new TranscriptTurn("t6", "candidate", "We moved to a sharded cluster and cut p99 latency from 800ms to 300ms."),
    };
}
