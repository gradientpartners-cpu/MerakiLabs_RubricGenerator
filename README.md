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
candidate transcript ──▶ POST /evaluations ──▶ grade ──────────────────┘
        │
        ▼
  redact PII ▶ per-criterion LLM judgment ▶ validate evidence ▶ deterministic
  weighted composite ▶ persisted result + audit trail
        │
        ▼
  GET /applications/{id}   ·   GET /applications/{id}/audit
```

> `POST /evaluations` grades **synchronously** and returns the full result in one call —
> the demo path, so the caught hallucination is visible live (see "See it work" below). The
> **production/scale** design is the async route already built under `Worker/`: a request
> enqueues onto a bounded `Channel<T>` and a `GradingWorker` `BackgroundService` grades out
> of band. Both call the same self-contained `GradePipeline`; the sync endpoint just runs it
> inline. (The read routes are `/applications/{id}` — the grade store's resource name.)

## What makes a grade trustworthy (the crux)
- **The LLM never computes the score.** Per criterion it returns `{score, verbatim
  evidence, confidence}`; the composite is a deterministic weighted sum in code.
- **Hallucination is caught, not hoped against.** A cited evidence span must be a
  verbatim substring of the cited transcript turn, or the criterion is rejected.
- **Uncertainty routes to humans.** Abstention and low confidence send a criterion
  to `needs_human_grading`; the system never invents a grade.
- **Everything is auditable.** Each score traces evidence → criterion → approved
  rubric version, in a dedicated audit trail.

## Every failure mode has a named test

The claim isn't "it handles errors" — it's that each specific way a grade can go wrong
has a defined, default-safe behavior **and a test that fails loudly if that behavior
regresses**. Run any row with `dotnet test --filter "FullyQualifiedName~<TestName>"`.

| Failure mode | System behavior | Proving test |
|---|---|---|
| LLM unavailable / unparseable | criterion → `Errored`, run → `NeedsHumanGrading`, composite `null` (never a fabricated score) | `GradePipelineTests.LlmUnavailable_RoutesToHuman_NeverFabricates` |
| **Paraphrased** evidence (true & on-topic, but not verbatim) | rejected `EvidenceNotFound`, score dropped — this is *why* matching is strict, not fuzzy | `GradePipelineTests.ParaphrasedEvidence_SemanticallyTrue_ButNotVerbatim_IsCaught` |
| Fabricated evidence (span absent from transcript) | rejected `EvidenceNotFound`, score dropped, run → human | `GradePipelineTests.FabricatedEvidence_FlagsCriterion_AndRoutesToHuman` |
| Hallucinated turn id (cites a turn that doesn't exist) | `EvidenceNotFound` (citation unverifiable) | `ValidateTests.CitedTurnThatDoesNotExist_IsEvidenceNotFound` |
| Citation missing on a confident grade (no span / no turn id) | `EvidenceNotFound` — an uncited score is never trusted | `ValidateTests.MissingTurnId_OnAConfidentGrade_IsEvidenceNotFound` |
| Abstention ("insufficient evidence") | `Abstained`, routed to human; first-class, not an error | `GradePipelineTests.Abstention_RoutesToHuman` |
| Low confidence (even with valid evidence) | `LowConfidence`, routed to human (confidence is triage, not correctness) | `GradePipelineTests.LowConfidence_RoutesToHuman_EvenWithValidEvidence` |
| Malformed rubric (weights don't sum to 1.0) | throws rather than emit a composite over a malformed rubric | `AggregateTests.WeightsNotSummingToOne_Throws` |

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

## See it work — the demo moments

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

**3. The caught hallucination, live over HTTP.** With the server running, grade the
canonical transcript against the seeded approved rubric in **one synchronous call**:

```bash
curl -s -X POST http://localhost:5026/evaluations \
  -H 'Content-Type: application/json' -H 'X-Tenant-Id: tenant-acme' \
  -d '{
    "rubricVersionId": "rub-backend-screen-v3",
    "artifactId": "artifact-7731",
    "turns": [
      {"turnId":"t1","speaker":"interviewer","text":"Walk me through a system you owned end to end."},
      {"turnId":"t2","speaker":"candidate","text":"I owned our billing pipeline for two years, from ingestion to invoicing."},
      {"turnId":"t3","speaker":"interviewer","text":"How did you handle a major incident?"},
      {"turnId":"t4","speaker":"candidate","text":"During a Postgres failover I coordinated the rollback and wrote the postmortem myself."},
      {"turnId":"t5","speaker":"interviewer","text":"Tell me about scaling."},
      {"turnId":"t6","speaker":"candidate","text":"We moved to a sharded cluster and cut p99 latency from 800ms to 300ms."}
    ]
  }'
```

The response grades `communication` (4) and `technical_depth` (5) from verbatim quotes,
but the `ownership` criterion cites *"I personally rebuilt the entire authentication
system"* — absent from turn t4 — so it is rejected (`EvidenceNotFound`), its score
dropped, `compositeScore` is `null`, and `state` is `NeedsHumanGrading`. Take the
returned `id` and `GET /applications/{id}/audit` to see the full reconstructable chain
(this is exactly [`examples/audit-trace-example.json`](examples/audit-trace-example.json)).
This is the **sync demo path**; the same `GradePipeline` runs out of band on the async
`Channel<T>` + `GradingWorker` route built for production scale.

*Tenant isolation is real, not asserted.* Take the returned `id` and read it back under
its own tenant, then under a different one — the second returns **404**, because every
repository read is scoped by `tenantId` (the single enforcement point, CLAUDE.md §3.6):

```bash
curl -s -o /dev/null -w '%{http_code}\n' -H 'X-Tenant-Id: tenant-acme' http://localhost:5026/applications/<id>  # 200
curl -s -o /dev/null -w '%{http_code}\n' -H 'X-Tenant-Id: tenant-other' http://localhost:5026/applications/<id>  # 404
```

**4. Adverse impact (four-fifths rule) tripping.** With the server running:

```bash
curl http://localhost:5026/fairness/adverse-impact
```

Computes the selection rate per group over seeded synthetic data and flags any group
below 80% of the top group's rate. `group_c` (ratio 0.50) trips the flag; `anyFlagged`
is `true`. (The endpoint computes over the seed only — there is no `rubric_version_id`
parameter; per-rubric slicing stays design-doc-only, DESIGN.md §11.)

**5. (bonus) A proxy attribute caught at rubric approval.** Generate a draft rubric,
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
