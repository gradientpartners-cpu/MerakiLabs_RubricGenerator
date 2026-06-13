# Decision 0003 — Add a synchronous demo grade endpoint; make docs match code

**Date:** 2026-06-13 · **Status:** accepted · **Relates to:** PRD §6, README "See it work"

## Context
A pre-presentation cross-check of the PRD against the built code surfaced a real gap:
the **grader — the deep slice — had no live HTTP trigger.** It was exercised only by its
xUnit suite. Worse, the docs advertised an API that didn't exist:

- PRD §6 / README promised `POST /evaluations` and `GET /evaluations/{id}`(`/audit`).
- The code served the async producer-less worker plus reads at `GET /applications/{id}`
  (`/audit`). `POST /evaluations` was never wired; the `Channel<T>` + `GradingWorker`
  path had **no HTTP producer**.

So the single most important capability (a high-stakes grade with caught hallucination)
could not be shown live, and the docs/code disagreed on route names — both bad looks the
day before a defensibility-focused review.

## Decision
Add a **synchronous** `POST /evaluations` that runs `GradePipeline` **inline** and returns
the full result (composite + every per-criterion score and validation status) in **one
HTTP call**. Explicitly the **demo path**, commented as such in code. The async
`Channel<T>` + `GradingWorker` route is the **production/scale** design and is left fully
intact — the sync endpoint just bypasses the queue. Both call the same self-contained,
DB-free pipeline.

Resolve the route-name mismatch by **making docs match code, not renaming code**: reads
stay `/applications/{id}`; only the genuinely-missing trigger was added at `/evaluations`.

## Why
- **Restores the headline demo.** The caught hallucination (t4 fabricated-auth ownership
  claim → `EvidenceNotFound` → score dropped → null composite → `NeedsHumanGrading`) is
  now reproducible live over HTTP from a clean clone, matching
  `examples/audit-trace-example.json` byte-for-byte.
- **No churn the day before the deadline.** Renaming `/applications`→`/evaluations`
  everywhere is risk for zero functional gain; aligning the docs is honest and cheap.
- **Keeps the scale story honest.** Sync-vs-async is a deliberate, commented distinction,
  not an accident — the production path stays the async queue/worker; the sync endpoint is
  flagged as the demo trigger in code, README, and PRD.

## Trade-off considered (honestly)
A synchronous grade endpoint blocks the request for the full grade and isn't how you'd run
this at scale — which is exactly why the async path exists and stays. Shipping *both* and
labelling which is which is more defensible than quietly making the demo path look like the
production design. The alternative (only the async path + a polling demo) reads worse live:
async timing awkwardness obscures the very thing worth showing.

## Consequences
- New: `RubricGrader/Grading/GradeDemo.cs` (canonical approved rubric `rub-backend-screen-v3`
  + t1–t6 transcript), seeded at startup **in the in-memory path only** (the postgres path
  stays clean, expecting rubrics via the real `/jd`→`/approve` lifecycle).
- New: committed `grade-criterion` replay fixtures → the demo runs zero-credential.
- New: `GradeEndpointTests` proves artifact-in → graded-result-out incl. the hallucination
  case, run against the committed fixtures. Suite 74 → 75 green.
- Docs: README diagram + "See it work" + PRD §6 aligned to the real routes and the
  sync-demo/async-production split; noted that `/fairness/adverse-impact` computes over the
  seed only (no `rubric_version_id` param — per-rubric slicing stays design-doc-only, §11).

## Process note (guardrail override)
This was driven from a "scratch thread" whose standing rule was **do not write to `/ai`**.
Editing PRD §6 (under `/ai`) was authorized explicitly and narrowly ("align the README
diagram + PRD §6 to reality"), and confirmed again for this log entry. Recording it rather
than silently editing `/ai` keeps the override itself auditable.
