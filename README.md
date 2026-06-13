# MerakiLabs · Rubric Generator + Grader

PS2 work-trial slice for an AI hiring pipeline. The **deep slice is the rubric
grader** — it scores a candidate transcript against an approved rubric, with
**cited, validated evidence** and **no fabricated grades**. A **thin JD→rubric
generator** sits on top to demonstrate the end-to-end pipeline.

> Full system design: [`DESIGN.md`](DESIGN.md) · AI build log: [`ai/`](ai/) ·
> standing rules: [`CLAUDE.md`](CLAUDE.md)

## The pipeline

```
JD ──▶ POST /jd ──▶ draft rubric ──▶ POST /rubrics/{id}/approve ──▶ approved rubric
                                                                        │
candidate transcript ──▶ POST /evaluations ──▶ async grade ────────────┘
        │
        ▼
  redact PII ▶ per-criterion LLM judgment ▶ validate evidence ▶ deterministic
  weighted composite ▶ persisted result + audit trail
        │
        ▼
  GET /evaluations/{id}   ·   GET /evaluations/{id}/audit
```

## What makes a grade trustworthy (the crux)
- **The LLM never computes the score.** Per criterion it returns `{score, verbatim
  evidence, confidence}`; the composite is a deterministic weighted sum in code.
- **Hallucination is caught, not hoped against.** A cited evidence span must be a
  verbatim substring of the cited transcript turn, or the criterion is rejected.
- **Uncertainty routes to humans.** Abstention and low confidence send a criterion
  to `needs_human_grading`; the system never invents a grade.
- **Everything is auditable.** Each score traces evidence → criterion → approved
  rubric version, in a dedicated audit trail.

## Stack
.NET 8 / ASP.NET Core (minimal API) · EF Core + Npgsql · in-process async grading via
`BackgroundService` + `Channel<T>` (no heavyweight job framework) · Claude behind an
`ILlmClient` adapter (`Replay` | `Live`) · xUnit + FluentAssertions.

## Run it (zero credentials)
Everything below runs against **recorded LLM fixtures** — no API key, no database.
`Llm:Backend` defaults to `replay` and persistence defaults to in-memory.

```bash
# requires the .NET 8 SDK (pinned via global.json)
dotnet build                       # restores + builds the solution
dotnet test                        # runs the full xUnit suite (74 tests)
dotnet run --project RubricGrader  # serves the API on http://localhost:5026
```

**No seed step.** The grader's tests generate their LLM fixtures at runtime; the
generator and fairness demos ship with committed fixtures under
`RubricGrader/Fixtures/`. A clean clone has everything it needs.

To call Claude (`claude-sonnet-4-6`) for real instead of replaying fixtures, opt in
via env vars (the only path that needs a key):

```bash
Llm__Backend=live ANTHROPIC_API_KEY=sk-... dotnet run --project RubricGrader
```

> The default persistence is in-memory. The `ConnectionStrings:Postgres` value in
> `appsettings.json` (`Password=postgres`) is a throwaway **local-dev default**, not a
> real credential — it is only read on the opt-in `Persistence:Backend=postgres` path.

## See it work — the four demo moments

**1 + 2. The grader (deep slice): a real grade, and a caught hallucination.**
The grader is proven by its end-to-end test suite (no server needed):

```bash
dotnet test --filter "FullyQualifiedName~GradePipelineTests"
```

- `DeterminismProof_SameArtifactGradedTwice_IdenticalComposite` — the **happy-path
  grade**: per-criterion LLM scores → deterministic weighted composite `4.4`,
  byte-for-byte reproducible (the LLM never computes the score).
- `FabricatedEvidence_FlagsCriterion_AndRoutesToHuman` — the **caught hallucination**:
  a cited span that isn't a verbatim substring of the transcript is rejected
  (`EvidenceNotFound`), the score is dropped, and the run goes to
  `NeedsHumanGrading` — it never fabricates a grade.

**3. Adverse impact (four-fifths rule) tripping.** With the server running:

```bash
curl http://localhost:5026/fairness/adverse-impact
```

Computes the selection rate per group over seeded synthetic data and flags any group
below 80% of the top group's rate. `group_c` (ratio 0.50) trips the flag; `anyFlagged`
is `true`.

**4. (bonus) A proxy attribute caught at rubric approval.** Generate a draft rubric,
then approve it — the protected-attribute denylist flags a criterion and the flag is
resurfaced at approval so the human gate never signs off blind:

```bash
# generate a draft (note the rubric.id and fairnessFlags in the response)
curl -s -X POST http://localhost:5026/jd -H 'Content-Type: application/json' \
  -d '{"jobDescription":"Senior backend engineer with strong distributed systems experience."}'

# approve it (use the id from above); the response repeats fairnessFlags for the approver
curl -X POST http://localhost:5026/rubrics/<rubric-id>/approve -H 'X-User-Id: you@example.com'
```

Both responses carry `fairnessFlags: [{ "criterionKey": "youthful_energy",
"matchedTerm": "age" }]` — flagged, never auto-dropped.
