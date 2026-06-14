# AI Build Log — session history (the messy middle, not a highlight reel)

This is the real, ordered history of how I drove the AI IDE (Claude Code, Opus) to
plan this repo. It deliberately includes the wrong turns, the renames, and the
scope changes — because the assignment is testing *how I think with AI*, not a clean
transcript. Tooling: **Claude Code (claude-opus model) in the terminal**, with a
`CLAUDE.md` rules file as the standing contract. Model for the product itself:
`claude-sonnet-4-6` behind a `replay|live` adapter.

---

## Entry 0 — framing the assignment
**Prompt (me):** pasted the full Meraki PS1/PS2 brief, "let's analyse."
**AI:** broke down what's actually graded (build log > design doc > scoped slice),
and **recommended PS1** (multi-tenant CRM) because my background (CashDesk,
multi-tenant SaaS) maps onto PS1's multi-tenant-isolation crux.
**Course-correction #1:** I overrode it — **"we are PS2."** The AI had optimised for
"play to your demonstrated strength" but I wanted the hiring-system problem.
*Lesson: the AI's first recommendation anchored on my résumé, not my intent. Stating
intent plainly beat letting it infer.*

## Entry 1 — choosing the slice + clarifying round
**Prompt (me):** confirmed PS2; asked it to pick the highest-value slice.
**AI:** proposed the **rubric grader** as the deep slice (high-stakes output =
strongest anti-hallucination story) and ran a **clarifying-question round** with
proposed defaults (artifact format, evidence-match strictness, scale, replay vs live,
slice boundary, which fairness mechanics to build, auth stubbing).
**Me:** "go with defaults" + **two overrides**:
- Confidence is a **triage signal, not a calibrated correctness measure** — frame it
  that way in code + docs; its only job is routing uncertain cases to humans.
- **Build** a minimal four-fifths (80%) adverse-impact computation over seeded
  synthetic data (mechanism demo), not just design-doc it. Full audit report stays
  design-only.
*Lesson: defaults-with-rationale let me approve fast and override surgically, instead
of re-specifying everything.*

## Entry 2 — docs before code (enforced ordering)
**Prompt (me):** explicit instruction — create `/ai`, draft `CLAUDE.md` *with me*,
then a PRD, **before any grader code**. Ask questions before starting.
**AI:** wrote `CLAUDE.md` (stack, the non-negotiable §3 constraints, scope guardrails,
conventions) and `ai/prd-grader.md`.
*Lesson: front-loading the rules file is what stops the AI drifting from the
architecture later. This paid off in Entry 4.*

## Entry 3 — naming churn (a genuinely messy stretch)
- I first gave the folder name `MirakiLabs_RubricGrader`.
- AI flagged the spelling: brief + recruiter email say **Meraki**, not Miraki.
- I confirmed "MerakiLabs," and pasted the **GitHub remote**:
  `github.com/gradientpartners-cpu/MerakiLabs_RubricGenerator`.
- **Conflict the AI caught:** the repo is named *RubricGenerator*, but the slice we'd
  scoped was the *grader*. It refused to silently assume and asked which was intended
  rather than renaming things to match a guess.
*Lesson: the AI surfacing the Generator-vs-Grader mismatch instead of papering over it
prevented a self-contradictory submission. This is the behaviour I want — flag, don't
guess.*

## Entry 4 — scope change: build BOTH modules
**Prompt (me):** "we are building 1, 2" → the JD→rubric **generator** AND the rubric
**grader**. Then the key constraint: **"grader stays the deep slice; add a thin
generator on top... Don't replace grader with generator — that trades your strongest
safeguard story (hallucination detection on a high-stakes output) for a lower-stakes
one."**
**AI:** reconciled all three docs to the new scope — grader = deep, generator = thin,
low-stakes *because* of the human-approval gate. Surgically edited my authored
`DESIGN.md` (flipped ~8 "designed, not built" mentions to "thin, built") while
preserving voice, and listed every change.
*Lesson: because `CLAUDE.md` already encoded "never trade grader depth," the AI applied
the new scope without diluting the core. The rules file did its job.*

## Entry 5 — language switch: Python → C#/.NET (caught my own reflex)
**Prompt (me):** switch the whole project from Python/FastAPI to C#/.NET 8 — and
record it honestly, don't bury it.
**The realisation:** we'd scaffolded in Python by reflex. But C#/.NET is my *actual*
production backend language; Python in my world is only the ML sidecar. **This slice
has no ML** — it's backend orchestration. So Python bought nothing, and .NET buys me
the language I defend fluently under the live grilling ("code you can't explain" is a
named red flag).
**AI:** wrote decision record `0002-switch-to-csharp.md`, then re-scaffolded on .NET 8
/ ASP.NET Core with a 1:1 stack remap, preserving *every* locked constraint. One thing
I told it to flag rather than decide: Redis/ARQ → `BackgroundService` + `Channel<T>`
in-process queue, **no Hangfire** unless durable retries genuinely earn it (a heavy job
framework for one slice edges toward the over-engineering red flag).
*Lesson: the honest move was admitting the Python start was a reflex, not a decision —
and that the studio-leans-Python signal is outweighed by defensibility + the fact that
there's no ML here. Catching it myself is the signal, not hiding that it happened.*

## Entry 6 — pre-presentation gap check: the grader had no live trigger
**Prompt (me):** scratch thread for presentation prep — "cross-check the plan we started
with; what's pending?" Standing rule for the thread: **don't write to `/ai`, ask before
recording anything.**
**AI:** cross-checked PRD §6 against the built code and surfaced a real gap — the **deep
slice had no HTTP trigger** (only tests), and the docs promised `POST /evaluations` +
`GET /evaluations/{id}` that didn't exist; the code served `GET /applications/{id}` and an
async worker with **no producer**.
**Me (the fix brief):** close it with the **minimal, demo-safe** version — add a
**synchronous** `POST /evaluations` that runs the pipeline inline and returns the full
result in one call; **keep the async `Channel<T>`+worker path intact** and comment that
sync = demo, async = production; **do NOT rename** `/applications` (churn) — make *docs
match code* except the new trigger; wire committed replay fixtures so it runs zero-cred;
confirm the t4 fabricated-auth case flows through end-to-end; add a test; align README +
PRD §6; then clone-test and push.
**AI:** built `GradeDemo` + the sync endpoint (commented as the demo path) + 3 committed
fixtures + an endpoint test (74→75 green), aligned README/PRD, verified the live caught
hallucination from a **fresh clone**, committed, and pushed. Wrote decision
[`0003`](../decisions/0003-sync-demo-grade-endpoint.md).
**Course-correction caught:** the post-clone shell `cd` had left the working dir stale, so
the first `git push` ran against the *wrong repo* and reported "up-to-date." Caught it by
checking `HEAD`/`origin/main` mismatch and re-pushed from the rubric repo explicitly —
flagged rather than trusting the green-looking output.
*Lesson: a "what's pending?" pass right before the demo paid for itself — the headline
capability wasn't reachable live. And the docs-match-code call (vs renaming) kept a
last-minute fix from becoming last-minute churn.*

---

## Entry 7 — comprehension pass + README consistency fix (study session, no feature work)
**Prompt (me):** a study/interview-prep thread — walk the built code to *understand* it
(not extend it): how the grade pipeline selects a "model", why a criterion score comes back
`null`, what `validationStatus` means, and what the grade is evaluated against. Then test
the API by hand with curl.
**AI:** traced it end-to-end from the source: `modelId` is **not selected** by any logic —
it's a hardcoded audit *label* (`GradeDemo.ModelId`), and `LiveClaudeClient` is still a
stub; `score: null` on `ownership` is the safeguard working (`EvidenceNotFound` → score
dropped in `GradePipeline.ToResult` → composite null → `NeedsHumanGrading`); the grade runs
against the **pre-seeded** approved rubric `rub-backend-screen-v3`, not a generated one.
**Found while testing:** the README's manual-test steps were inconsistent — the
**jd/approve** examples omitted the tenant header (falling back to `tenant-demo`) while the
**grade** example used `tenant-acme`, so a reviewer running them in sequence would
dead-end; and the suite count was stale (74, actually 76).
**Me (the fix brief):** minimal, doc-only — standardize **every** curl example to
`X-Tenant-Id: tenant-acme`, bump 74→76, add one sentence clarifying the seeded vs generated
rubric are separate flows, and add a clean nine-command "test the API" section with
per-command descriptors + placeholder notes. **Do not touch app code; no structural
rewrite.** Then clone-clean run all nine in sequence, build, suite green, commit + push.
**AI:** made the three README edits, rebuilt (76 green), ran all nine commands in sequence
under `tenant-acme` (draft → approve → grade → audit → fairness → isolation 200/404, no
dead-ends), committed **README only**, pushed.
**Course-correction caught:** before committing, `git status` showed a stray, *pre-existing*
working-tree edit in `GenerationValidation.cs` — `"religio"` had been changed to
`"religion"` in the protected-attribute denylist. Flagged it rather than sweeping it into
the commit (the brief was docs-only). On review it's a **regression**: `"religio"` is a
substring match that catches `religion`/`religious`/`religio-`; `"religion"` only matches
the exact word, so "religious background" in a JD would stop being flagged.
**Decision:** **keep `"religio"`, discard the `"religion"` edit** (`git checkout`'d the
file). Broader denylist coverage is the safer default; the term flags for a human, never
auto-drops, so over-matching costs nothing.
*Lesson: reading your own code back is cheap insurance — it surfaced a doc inconsistency
that would have tripped the reviewer and a silent denylist regression that had nothing to
do with the task. Commit scope discipline (README only) kept the two concerns separate.*

---

## Open threads / things still unbuilt
- Repo name foregrounds "Generator" though the *deep* slice is the grader — left as-is
  since both are now built; flagged for the live review.
- No code committed yet; .NET skeleton being scaffolded now (Python skeleton removed).
- Regulatory specifics (LL144 cadence, EU AI Act timelines) intentionally NOT stated
  as fact — to be verified live, not recited from a stale memory.
- Open decision deferred to me: whether the in-process `Channel<T>` queue ever needs to
  become durable (Hangfire / a real broker). Not for this slice.

> Continue appending here as prompts succeed AND fail. The failures are the point.
