# PRD — Rubric Grader (deep) + JD→Rubric Generator (thin)

**Status:** draft · **Owner:** Manasa · **Scope:** the built modules of PS2 — the
**grader** (deep, where the senior signal lives) and a **thin generator** on top to
show the end-to-end pipeline. The grader is never traded for the generator.
**Read alongside:** `/CLAUDE.md` (§3 constraints are binding here).

---

## 1. Problem & goal

Given a candidate **artifact** (a transcript — stub for the AI video screen) and a
**pre-approved, versioned rubric**, produce a **per-criterion, evidence-cited,
auditable evaluation** and a **deterministic composite score**, without the LLM
ever fabricating a grade or the final number.

The grader **assists**; it does not decide. Its output feeds a human reviewer.

### Success criteria
- Every per-criterion score is backed by a verbatim evidence span that is
  **verifiably present** in the artifact, or the criterion is explicitly abstained /
  routed to a human.
- The composite score is **reproducible** from stored inputs (deterministic code).
- A hallucinated citation is **caught and rejected**, never scored.
- The full chain `score → evidence → criterion → rubric version` is auditable.
- The README runs the demo **with zero credentials** (replay adapter).

### Non-goals (design-doc-only — see CLAUDE.md §4)
Human decision UI + final hire decision · full LL144/EU-AI-Act audit report ·
real auth · real video/biometrics · any frontend · JD-parsing sophistication
(the generator does one structured call, no multi-step extraction).

## 1a. Generator (thin) — also built

Given a **JD** (text), produce a **draft rubric**: weighted, defensible criteria
each with 1–5 behavioral anchors and a short rationale. The draft is persisted as
`status=draft` and requires **human approval** (`POST /rubrics/{id}/approve`) before
it becomes an immutable, versioned input the grader can consume.

- **One structured LLM call** through the same Claude adapter (`replay|live`).
- **Weights are normalised in code**, never trusted from the LLM (same "LLM doesn't
  own the number" discipline as the grader's composite).
- **Auditable**: store the JD, model id, prompt version, and raw LLM response.
- **Low-stakes by design**: the human-approval gate catches generator mistakes, so it
  does NOT carry the grader's evidence-validation / abstention machinery.
- **Thin means thin**: no UI, no JD section parsing, no iterative refinement. If
  effort must be cut, cut it here — never from the grader.

## 2. Users
- **Recruiter/hiring admin** (out of slice): uploads JD, approves rubric, reviews
  results. Here represented only by seeded rubric fixtures + the result endpoint.
- **System** (in slice): grades async and writes the audit trail.

## 3. Core flow

```
POST /evaluations  (artifact_ref + rubric_version_id, tenant-scoped)
      │
      ▼
[redact PII / name-blind the artifact]          ← built, pre-LLM
      │
      ▼
enqueue on Channel<T> (BackgroundService worker)  →  state: queued
      │
      ▼   for each rubric criterion:
   Claude adapter (replay|live) → structured output
        { score, evidence (verbatim), confidence }
      │
      ▼
[boundary validation]  normalize → exact substring match within cited turn
      │  pass            │ fail / abstain / low-confidence
      ▼                  ▼
 accept criterion    flag criterion → needs_human_grading
      │
      ▼
[deterministic weighted aggregation in code]    ← LLM never does this
      │
      ▼
persist EvaluationResult + append AuditTrail
state: graded  |  needs_human_grading  |  failed
```

### State machine
- `queued` → grading started.
- `graded` → all required criteria validated + composite computed.
- `needs_human_grading` → ≥1 required criterion abstained / low-confidence /
  failed validation, OR partial LLM failure. **Default-safe state.**
- `failed` → LLM unreachable / unparseable for the whole job (degradation, §3.5).

## 4. Data model (initial)

- **tenant** `(id, name)` — isolation root.
- **rubric_version** `(id, tenant_id, version, criteria[], approved_by, approved_at)`
  immutable; `criteria` = `[{key, label, weight, anchors{1..5}}]`.
- **artifact** `(id, tenant_id, blob_ref, transcript_turns[])`
  `transcript_turns` = `[{turn_id, speaker, text}]` (post-redaction stored separately).
- **evaluation** `(id, tenant_id, artifact_id, rubric_version_id, state,
  composite_score, model_id, prompt_version, created_at)`.
- **criterion_result** `(id, evaluation_id, criterion_key, score, confidence,
  evidence_turn_id, evidence_span, validation_status, abstained)`.
- **audit_event** `(id, tenant_id, evaluation_id, kind, payload_json, created_at)`
  append-only; payload includes the **raw LLM response** for reproducibility.
- **candidate_demographic** `(evaluation_id, group)` — *synthetic, seed-only*, used
  solely by the four-fifths check. Kept separate from grading inputs by design.

All tables carry `tenant_id`; every read/write goes through the repository layer
which injects the tenant scope (CLAUDE.md §3.6).

## 5. Key components
- `Adapters/ILlmClient.cs` (+ `ReplayClaudeClient`, `LiveClaudeClient`) — the only
  callers of the LLM; `replay` + `live` behind one interface.
- `Generation/GenerationPrompt.cs` + `Generation/Normalize.cs` — versioned JD→rubric
  prompt; pure weight-normalisation + draft-shape validation.
- `Grading/GradingPrompt.cs` — versioned per-criterion grading prompt.
- `Grading/Validate.cs` — pure: normalization + substring-within-turn match.
- `Grading/Aggregate.cs` — pure: deterministic weighted composite.
- `Grading/Redact.cs` — pure: PII / name-blind redaction.
- `Fairness/AdverseImpact.cs` — pure: selection rate per group + 80%-rule flag.
- `Repository/` — tenant-scoped persistence (EF Core + Npgsql).
- `Worker/` — `BackgroundService` + `Channel<T>` job loop.
- `Api/` — minimal-API endpoint mapping + stubbed auth-context.

## 6. API surface (slice) — as built
- `POST /jd` → generate a **draft** rubric from a JD (generator, thin).
- `POST /rubrics/{id}/approve` → freeze draft as an immutable versioned rubric.
- `POST /evaluations` → grade an artifact against an approved rubric and return the full
  result (composite + per-criterion scores/statuses) **synchronously, in one call**
  (tenant from the stubbed auth ctx). This is the **demo trigger**: it runs the
  `GradePipeline` inline so the caught hallucination is visible live. The
  **production/scale** path is the async route already built under `Worker/` — a bounded
  `Channel<T>` (`IGradeQueue`) drained by a `GradingWorker` `BackgroundService` — left
  intact; the sync endpoint just bypasses the queue.
- `GET /applications/{id}` → result + per-criterion breakdown.
- `GET /applications/{id}/audit` → the reconstructable audit chain for that evaluation.
  (Read routes use `/applications` — the grade store's resource name. The grade *trigger*
  is `/evaluations`; reads/writes were not renamed to avoid churn.)
- `GET /fairness/adverse-impact` → per-group selection rate + four-fifths flags over
  seeded synthetic data (clearly labelled non-production). Computes over the seed only;
  there is no `rubric_version_id` parameter (per-rubric slicing stays design-doc-only, §11).

## 7. Anti-hallucination & fairness mechanics (the crux)
- **Validation** (CLAUDE.md §3.2): normalize (whitespace/quotes/case) → exact
  substring within the **cited turn**. Miss ⇒ caught hallucination ⇒ reject + flag.
- **Abstention** (§3.3): "insufficient evidence" is a valid, expected output.
- **Confidence** (§3.4): triage-only, **uncalibrated**; `low` ⇒ route to human.
  Documented so it never reads as a correctness measure.
- **Degradation** (§3.5): never fabricate; fall back to a human state.
- **Redaction**: strip candidate name / contact / demographic-revealing tokens
  before the artifact reaches the LLM.
- **Adverse-impact (minimal)**: over seeded synthetic candidates, compute
  selection rate per group and flag any group below 80% of the top group's rate.
  Mechanism demo only — production note documented inline.

## 8. Testing plan (invariants as tests)
1. Hallucinated evidence (span not in artifact) ⇒ criterion rejected + flagged.
2. Valid evidence ⇒ criterion accepted; composite matches hand-computed weights.
3. Abstention ⇒ criterion routed to `needs_human_grading`.
4. `low` confidence ⇒ routed to human even if a span validates.
5. LLM unparseable/unreachable ⇒ evaluation `failed`/`needs_human_grading`, no
   fabricated score.
6. Cross-tenant read attempt ⇒ blocked at repository layer.
7. Aggregation is pure & deterministic (same inputs ⇒ same composite).
8. Four-fifths math: known fixture ⇒ expected flags.

## 9. Open questions / risks
- Evidence match strictness vs. abstention rate — strict match may over-route to
  humans; acceptable for a trial, note as a tuning knob.
- Prompt versioning discipline — must bump `prompt_version` whenever the grading
  prompt changes, or reproducibility claims weaken.
- Synthetic demographic data must never be confused for production signal — keep
  it visibly labelled and isolated.

## 10. Deferred / "with two more weeks"
Reviewer UI + human decision capture · deepening the generator (JD section parsing,
proxy-attribute linting of generated criteria, iterative refinement) · calibration
study of LLM confidence · full LL144 audit report over real aggregates · inter-rater
/ multi-model agreement · prompt-injection hardening of artifacts.
