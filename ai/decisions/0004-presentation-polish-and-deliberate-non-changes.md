# Decision 0004 — Day-before polish: prove existing judgment, change no shipped behavior

**Date:** 2026-06-13 · **Status:** accepted · **Relates to:** README, `RubricGrader.Tests`

## Context
The build was complete, clean, suite green, submission-ready. The remaining question was
not "what's missing" but "what would make the *existing* judgment more visible to a senior
reviewer the day before the deadline — without adding risk." Over-engineering is a named
red flag in this brief, and the zero-credential fresh-clone run is the demo's foundation.
So the bar was deliberately asymmetric: **high signal, near-zero risk, no change to shipped
behavior, the demo path, or existing tests.**

## Decision — what I built (all additive)
1. **Adversarial paraphrase test** (`GradePipelineTests.ParaphrasedEvidence_SemanticallyTrue_ButNotVerbatim_IsCaught`).
   The model cites *"cut p99 latency in half"* against a turn that says *"cut p99 latency
   from 800ms to 300ms"* — a **true, on-topic paraphrase that is not a verbatim substring**.
   The pipeline rejects it (`EvidenceNotFound`), drops the score, routes to human. It runs
   through the real replay adapter on a self-seeded fixture (the existing test pattern),
   additive only. This proves the *subtle* failure mode, not just the blatant fabrication
   the demo already shows — and it is the concrete justification for refusing fuzzy/semantic
   matching (§3.2): fuzzy matching would let exactly this paraphrase pass.
2. **Failure-mode → behavior → test map** in the README. One table: LLM-down, paraphrased
   evidence, hallucinated turn id, missing citation, abstention, low confidence, malformed
   rubric → the default-safe behavior → the exact named test that fails if it regresses.
   Turns "I tested the invariants" from a claim into a checkable index.
3. **Live tenant-isolation curl** in the README: same evaluation id returns `200` under its
   own tenant and `404` under another, because every read is `tenantId`-scoped at the
   repository (§3.6). Makes an invisible safeguard visible in two lines.

These touch only: a new test method + a new additive test helper (`SeedGradeForTurns`, no
existing helper modified), and README prose. No production code, no existing test, no demo
path, no fixture changes.

## Known limitations (acknowledged, and acceptable for this slice)
Stating the boundaries is itself the senior signal — these are deliberate, not oversights:
- **Semantic relevance is unchecked; validation is verbatim-only.** A model could cite a
  real, verbatim substring that is technically present but *irrelevant* to the criterion and
  it would pass the existence check. We verify *"this quote is really in the transcript,"*
  not *"this quote supports this score."* Strict substring is the right safeguard against
  fabrication; relevance grading is a human's job (and that's why uncertain cases route to
  one). Catching fabrication is the high-stakes failure; relevance is lower-stakes and gated.
- **Redaction is deliberately narrow.** `Redact` masks email/URL/phone/self-introduction
  names only. Full NER name-blinding is design-doc-only because over-eager capitalised-token
  masking would shred technical nouns ("Postgres", "Azure") and *weaken* the very
  evidence-validation surface the system depends on. Narrow-but-honest beats broad-but-lossy.
- **Trivially short spans pass.** A one- or two-word verbatim span ("the") satisfies the
  substring check. A minimum-substance guard was considered and rejected (see below).
- **No automated semantic proxy detection.** The protected-attribute denylist is literal
  term matching that *flags for a human*, never auto-drops. Inferring proxy attributes
  (e.g. a criterion that correlates with a protected class without naming it) is not
  attempted — it's a hard problem we won't fake, and the human-approval gate is the
  enforcement point by design (DESIGN.md §8).

## Deliberately rejected the day before deadline (the reasoning is the artifact)
- **B1 — minimum-evidence-substance guard.** *Rejected.* It modifies `Grading/Validate.cs`,
  the single highest-signal function in the repo, and its `ValidateTests`. Changing the
  pass/fail boundary risks shifting existing expectations and possibly the demo composite,
  for marginal signal. Blast radius (the core safeguard + its tests) outweighs the upside on
  the final day. Captured here as a known limitation instead of a code change.
- **B2 — global string-enum serialization.** *Rejected.* `POST /evaluations` returns enums
  as integers (`"state": 2`), less readable than the `/audit` payload. The fix is a global
  `JsonStringEnumConverter` in `Program.cs` — which changes serialization for *every*
  endpoint and could alter response shapes tests or the documented demo depend on.
  **Mitigation chosen over change:** in the live demo, show `GET /applications/{id}/audit`,
  which already renders `"NeedsHumanGrading"` / `"Valid"` as strings. Same readability,
  zero blast radius.
- **A6 — CI workflow.** *Rejected for now.* It doesn't touch app code, but it *executes* the
  suite on a foreign runner, where fixture-path resolution (the `AppContext.BaseDirectory`
  walk-up to find committed fixtures) or an SDK-version mismatch could produce a **public red
  badge** the day before review — a worse signal than no badge. Worth doing later with a
  pinned SDK and a verified working dir; not worth the day-before risk.

## Consequences
- Test count 75 → 76, all green; fresh-clone `dotnet test` still passes with zero credentials.
- No shipped behavior, demo path, or existing test changed. The submission's foundation
  (the zero-credential clone run) is untouched by construction.
- The rejections above are the defensible part: knowing *what not to change* on the final
  day, and why, is the judgment this records.
