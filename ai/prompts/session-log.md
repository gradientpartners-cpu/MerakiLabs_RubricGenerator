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
