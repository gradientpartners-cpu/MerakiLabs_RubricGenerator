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
dotnet test                        # runs the full xUnit suite (76 tests)
dotnet run --project RubricGrader  # serves the API on http://localhost:5026
```

**No seed step.** The grader's tests generate their LLM fixtures at runtime; the
generator and fairness demos ship with committed fixtures under
`RubricGrader/Fixtures/`. A clean clone has everything it needs.

> **Demo console.** A thin, single-file demo UI lives at [`demo/index.html`](demo/index.html) —
> start the server, then open that file in a browser to click through all six steps
> (health → generate → approve → grade → audit → fairness). It is a presentation aid over
> the existing API, not the product frontend (see `ai/decisions/0005-demo-console.md`).

To call Claude (`claude-sonnet-4-6`) for real instead of replaying fixtures, opt in
via env vars (the only path that needs a key):

```bash
Llm__Backend=live ANTHROPIC_API_KEY=sk-... dotnet run --project RubricGrader
```

> The default persistence is in-memory. The `ConnectionStrings:Postgres` value in
> `appsettings.json` (`Password=postgres`) is a throwaway **local-dev default**, not a
> real credential — it is only read on the opt-in `Persistence:Backend=postgres` path.

## See it work — test the API

Start the server (`dotnet run --project RubricGrader`), then run these nine commands in
order. **Every command uses the same tenant (`tenant-acme`)** so they chain cleanly and
nothing dead-ends. (The grader is also proven without a server by
`dotnet test --filter "FullyQualifiedName~GradePipelineTests"`.)

> **Two separate flows.** The grade demo (step 5) uses the **pre-seeded** rubric
> `rub-backend-screen-v3`; the jd→approve flow (steps 2–4) demonstrates **generating a
> new** rubric — they are independent, so the generated rubric does not chain into the
> grade step.
>
> **Placeholders.** Replace `rub-XXXX` with the `rubric.id` returned by step 2, and
> `eval-XXXX` with the `id` returned by step 5.

```bash
# 1. Health — server is up + which LLM/persistence backends are active.
curl -s http://localhost:5026/health | python3 -m json.tool

# 2. Generate a DRAFT rubric from a JD — note the youthful_energy/age fairness flag.
curl -s -X POST http://localhost:5026/jd \
  -H 'Content-Type: application/json' -H 'X-Tenant-Id: tenant-acme' \
  -d '{"jobDescription":"Senior backend engineer with strong distributed systems experience."}' | python3 -m json.tool

# 3. Approve the draft -> Approved (replace rub-XXXX with the id from step 2).
curl -s -X POST http://localhost:5026/rubrics/rub-XXXX/approve \
  -H 'X-Tenant-Id: tenant-acme' -H 'X-User-Id: manasa' | python3 -m json.tool

# 4. Rubric audit trail — shows the approval event (same id as step 3).
curl -s http://localhost:5026/rubrics/rub-XXXX/audit \
  -H 'X-Tenant-Id: tenant-acme' | python3 -m json.tool

# 5. Grade a transcript against the pre-seeded rubric — the caught EvidenceNotFound hallucination.
curl -s -X POST http://localhost:5026/evaluations \
  -H 'Content-Type: application/json' -H 'X-Tenant-Id: tenant-acme' \
  -d '{"rubricVersionId":"rub-backend-screen-v3","artifactId":"artifact-7731","turns":[{"turnId":"t1","speaker":"interviewer","text":"Walk me through a system you owned end to end."},{"turnId":"t2","speaker":"candidate","text":"I owned our billing pipeline for two years, from ingestion to invoicing."},{"turnId":"t3","speaker":"interviewer","text":"How did you handle a major incident?"},{"turnId":"t4","speaker":"candidate","text":"During a Postgres failover I coordinated the rollback and wrote the postmortem myself."},{"turnId":"t5","speaker":"interviewer","text":"Tell me about scaling."},{"turnId":"t6","speaker":"candidate","text":"We moved to a sharded cluster and cut p99 latency from 800ms to 300ms."}]}' | python3 -m json.tool

# 6. Read the stored result back (replace eval-XXXX with the id from step 5).
curl -s http://localhost:5026/applications/eval-XXXX \
  -H 'X-Tenant-Id: tenant-acme' | python3 -m json.tool

# 7. Full audit chain — enums render as readable strings (same id as step 6).
curl -s http://localhost:5026/applications/eval-XXXX/audit \
  -H 'X-Tenant-Id: tenant-acme' | python3 -m json.tool

# 8. Fairness — four-fifths rule; group_c flagged (anyFlagged true).
curl -s http://localhost:5026/fairness/adverse-impact | python3 -m json.tool

# 9. Tenant isolation — same id returns 200 for its tenant, 404 for another.
curl -s -o /dev/null -w 'own:   %{http_code}\n' -H 'X-Tenant-Id: tenant-acme'  http://localhost:5026/applications/eval-XXXX
curl -s -o /dev/null -w 'other: %{http_code}\n' -H 'X-Tenant-Id: tenant-other' http://localhost:5026/applications/eval-XXXX
```

**What step 5 proves:** it grades `communication` (4) and `technical_depth` (5) from
verbatim quotes, but the `ownership` criterion cites *"I personally rebuilt the entire
authentication system"* — absent from turn t4 — so it is rejected (`EvidenceNotFound`),
its score dropped, `compositeScore` is `null`, and `state` is `NeedsHumanGrading`. This
is the **sync demo path**; the same `GradePipeline` runs out of band on the async
`Channel<T>` + `GradingWorker` route built for production scale. The step-7 audit is
exactly [`examples/audit-trace-example.json`](examples/audit-trace-example.json).

**What steps 2–4 prove:** the protected-attribute denylist flags a criterion
(`fairnessFlags: [{ "criterionKey": "youthful_energy", "matchedTerm": "age" }]`) and the
flag is resurfaced at approval, so the human gate never signs off blind — flagged, never
auto-dropped.
