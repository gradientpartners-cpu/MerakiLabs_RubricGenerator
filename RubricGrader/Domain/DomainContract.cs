namespace RubricGrader.Domain;

// The domain contract — the record types every module shares (the C# equivalent of the
// old schemas.py). These types ENCODE the CLAUDE.md §3 constraints so they can't be
// quietly violated:
//   - the LLM returns per-criterion judgment only (CriterionGrade), never a composite;
//   - evidence is a verbatim span tied to a specific transcript turn;
//   - Confidence is a triage signal, NOT a calibrated correctness measure;
//   - abstention and hallucination are first-class outcomes.

// ---- transcript (stub for the AI video screen — text only, no biometrics) ----------

/// <summary>One speaker turn of the (redacted) transcript. Evidence is validated
/// against the text of the cited turn.</summary>
public sealed record TranscriptTurn(string TurnId, string Speaker, string Text);

// ---- rubric -------------------------------------------------------------------------

/// <summary>A weighted, behaviorally-anchored evaluation criterion.</summary>
public sealed record RubricCriterion(
    string Key,
    string Label,
    double Weight,                          // normalised in code; weights sum to 1.0
    IReadOnlyDictionary<int, string> Anchors // descriptors for scores 1..5
);

public enum RubricStatus
{
    Draft,    // produced by the generator, awaiting human approval
    Approved  // frozen, immutable, usable by the grader
}

/// <summary>A non-blocking fairness signal raised by the generator's protected-attribute
/// denylist: one criterion that matched a protected-attribute term, with the term that
/// tripped it. Persisted on the Draft and resurfaced at approval time so the human gate —
/// the actual fairness enforcement point (DESIGN.md §8) — never approves blind. It flags;
/// the reviewer adjudicates. It never drops a criterion.</summary>
public sealed record FairnessFlag(string CriterionKey, string MatchedTerm);

/// <summary>An immutable, versioned rubric. The grader consumes only Approved versions
/// (CLAUDE.md §8).</summary>
public sealed record RubricVersion(
    string Id,
    string TenantId,
    int Version,
    RubricStatus Status,
    IReadOnlyList<RubricCriterion> Criteria,
    string? ApprovedBy = null,
    DateTimeOffset? ApprovedAt = null,
    IReadOnlyList<FairnessFlag>? FairnessFlags = null
);

/// <summary>One criterion as proposed by the thin generator's single LLM call. RawWeight
/// is re-normalised in code, never trusted from the model (CLAUDE.md §9).</summary>
public sealed record DraftCriterion(
    string Key,
    string Label,
    double RawWeight,
    IReadOnlyDictionary<int, string> Anchors,
    string Rationale
);

// ---- LLM structured output (judgment only) ------------------------------------------

/// <summary>Triage signal ONLY. LLM self-reported and NOT calibrated — High does not
/// mean correct (CLAUDE.md §3.4). Its sole job is routing Low cases to a human.</summary>
public enum Confidence { High, Medium, Low }

/// <summary>What the LLM returns for a single criterion. It never returns the composite
/// (CLAUDE.md §3.1). When Abstained is true ("insufficient evidence"), Score/Evidence
/// are null (CLAUDE.md §3.3).</summary>
public sealed record CriterionGrade(
    string CriterionKey,
    bool Abstained,
    int? Score,            // 1..5 when present
    string? Evidence,      // must be a verbatim span of the cited turn
    string? TurnId,
    Confidence? Confidence
);

// ---- grading results ----------------------------------------------------------------

/// <summary>Outcome of boundary validation for one criterion (CLAUDE.md §3.2–§3.4).</summary>
public enum ValidationStatus
{
    Valid,
    // Cited evidence is not a verbatim substring of the cited turn -> score rejected.
    // This names what we can prove: the citation is not verifiable against the transcript.
    // It is NOT a claim about model intent (we do a deterministic substring check, not
    // intent detection). Formerly "Hallucinated"; renamed for precision.
    EvidenceNotFound,
    Abstained,
    LowConfidence,
    Errored         // LLM unavailable/unparseable for this criterion -> never a fabricated score
}

/// <summary>A validated per-criterion result. Only Valid results contribute to the
/// composite.</summary>
public sealed record CriterionResult(
    string CriterionKey,
    int? Score,
    Confidence? Confidence,
    string? EvidenceTurnId,
    string? EvidenceSpan,
    ValidationStatus ValidationStatus
);

public enum EvaluationState
{
    Queued,
    Graded,
    NeedsHumanGrading,  // default-safe state for any uncertainty/failure
    Failed
}

/// <summary>The persisted grade for one evaluation run. CompositeScore is a deterministic
/// weighted aggregation computed in C# (CLAUDE.md §3.1) — reproducible from the
/// per-criterion scores + rubric weights alone.</summary>
public sealed record EvaluationResult(
    string Id,
    string TenantId,
    string ArtifactId,
    string RubricVersionId,
    EvaluationState State,
    double? CompositeScore,
    IReadOnlyList<CriterionResult> Results,
    string ModelId,
    string PromptVersion
);

// ---- audit --------------------------------------------------------------------------

/// <summary>Append-only audit record (CLAUDE.md §3.7). The trail records more than grades:
/// SubjectId is the entity the event is about — an evaluation id for grading events, a
/// rubric version id for a rubric-approval event — so a score traces to its rubric version,
/// which in turn traces to the human-accountable approval that froze it.</summary>
public sealed record AuditEvent(
    string Id,
    string TenantId,
    string SubjectId,
    string Kind,
    string PayloadJson,
    DateTimeOffset CreatedAt
);
