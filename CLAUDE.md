# CLAUDE.md — MerakiLabs_RubricGenerator (.NET 8)

This file is the contract between me (the engineer) and you (Claude). It encodes the
architecture, the non-negotiable constraints, the scope guardrails, and the working
conventions for this repo. **Read it fully before proposing or writing code. Do not
drift from the constraints in §3 — if a request appears to violate them, stop and
flag it rather than complying silently.**

> **Stack note:** this project was rebuilt from Python/FastAPI to **C#/.NET 8** — see
> `ai/decisions/0002-switch-to-csharp.md`. C#/.NET is my production backend language;
> Python is reserved for ML, and this slice has none. All §3 constraints carried over
> unchanged.

---

## 1. What this repo is

A **work-trial slice** for Meraki Labs (PS2 — Startup Hiring System). The north star
is a hiring pipeline: application → AI screen → rubric → decision. The whole system is
designed on paper (see `DESIGN.md`). **Two modules are built here:**

- **The rubric grader — the DEEP slice.** Takes a candidate artifact (a transcript —
  a stub for the AI video screen) and an approved rubric, and produces a
  per-criterion, evidence-cited, auditable evaluation. This is where the
  anti-hallucination + auditability work is proven and defended. It does **not** make
  the hire decision; a human does.
- **A thin JD→rubric generator — built ON TOP of, not in place of, the grader.** Takes
  a JD and produces draft weighted, defensible criteria, which a human approves before
  they become the grader's immutable rubric input. It demonstrates the end-to-end
  pipeline (`JD → draft → approve → grade → audit`). Its stakes are low *because* of
  the human-approval gate, so it deliberately does NOT carry the grader's heavy
  safeguards. **Never trade the grader's depth for the generator.** If the clock gets
  tight, the generator degrades to a fixed approved-rubric fixture.

This is a 2-day trial. **Scope is the test.** Graded deliverables: the systems-design
doc, the AI build log in `/ai`, this runnable slice, and a proposal doc. The build log
— real prompts, dead-ends, recoveries — is what they say they are *really* testing.
Keep `/ai` honest and current; never sanitize it.

## 2. Stack & rationale (1:1 remap from the original Python plan)

| Concern | Choice | Why |
|---|---|---|
| Web/API | **ASP.NET Core minimal API** (net8.0) | Small, explicit endpoint surface; no controller boilerplate for one slice. |
| Async grading | **`BackgroundService` + `System.Threading.Channels.Channel<T>`** | In-process queue. Grading is LLM-bound, so it must not block the request. **No Hangfire / heavyweight job framework** unless durable retries genuinely earn it — that would edge toward the over-engineering red flag. If durability is needed, **flag it for my decision; do not add it silently.** |
| Domain contract | **C# `record` types** | Immutable value semantics; the equivalent of the old Pydantic schemas. |
| LLM | **`HttpClient` behind `ILlmClient`** | The LLM is a swappable dependency. Two impls: `LiveClaudeClient` (real API) and `ReplayClaudeClient` (recorded JSON fixtures). Nothing else talks to Claude. |
| Persistence | **EF Core + Npgsql (PostgreSQL)** | Relational audit trail; FK integrity for `score → evidence → criterion → rubric version`. |
| Blob | Object storage (prod) | Raw artifacts by reference, not in DB rows. |
| Tests | **xUnit + FluentAssertions** | Readable assertions; pure-core tested with no DB/LLM. |
| Shape | **Modular monolith** | One deployable, folders/namespaces as module boundaries. NOT microservices. |

## 3. Hard architectural constraints (NON-NEGOTIABLE — carried over unchanged)

1. **The LLM never produces the composite score.** Per criterion the LLM returns
   structured judgment ONLY: `{ score: int 1–5, evidence: verbatim string,
   turnId: string, confidence: high|medium|low }`. It judges **one criterion at a
   time** against that criterion's anchors + the redacted transcript. The
   **composite is a deterministic weighted aggregation computed in C#** — reproducible
   from the per-criterion scores + rubric weights alone.

2. **Evidence validation = the anti-hallucination safeguard (highest signal).**
   Normalize the cited evidence and the source turn text on both sides (collapse
   whitespace, unify quote chars, casefold), then require an **exact substring match
   within the cited turn**. A near-miss is a **hallucinated citation → reject + flag
   that criterion, never score it.** NO fuzzy/semantic matching — fuzzy would let the
   model paraphrase its evidence and still pass, defeating the check. **Strictness is
   the safeguard.**

3. **Abstention is first-class.** The LLM may return "insufficient evidence" for a
   criterion instead of inventing a grade.

4. **Confidence is a TRIAGE signal, not a calibrated correctness measure.** It is
   LLM self-reported and **not reliable** — high confidence does NOT mean correct. Its
   only job is routing: `low` confidence (like abstention and failed validation) sends
   that criterion to `needs_human_grading`. **In comments and docs, never let
   confidence read as a measure of accuracy.**

5. **Graceful degradation, never fabrication.** LLM unavailable / output unparseable /
   validation fails for required criteria → the application goes to
   `needs_human_grading`. Never emit a fabricated or partial-as-whole score.

6. **Multi-tenant isolation in one place.** Every query is scoped by `tenantId`,
   enforced in the **repository layer** — not sprinkled across services. A query that
   can't be tenant-scoped doesn't ship. Auth is **stubbed** (an authenticated-context
   dependency maps a header/token → tenant), so identity is stubbed but the
   enforcement is real and testable.

7. **Auditability is first-class.** Every score traces
   `score → evidence span → rubric criterion → human-approved rubric version`. A
   dedicated, append-only audit trail. Evaluations are reproducible: record
   `rubricVersionId`, model id, prompt version, and the raw LLM response.

8. **Rubric is immutable once approved.** A rubric becomes the grader's input only
   after **human approval**, which freezes it as a versioned, immutable record. The
   thin generator produces a *draft* only; the grader consumes approved versions and
   never generates or mutates a rubric.

9. **The generator is thin; the grader is deep.** One structured LLM call → draft
   criteria → normalise weights **in code** → persist as draft awaiting approval. It
   reuses the grader's adapter, repository, and audit infrastructure. Do not grow it
   into a second deep module — if effort must be cut, cut the generator, never the
   grader. The generator degrades to a fixed approved-rubric fixture under time
   pressure.

## 4. Scope guardrails

**In scope (built):**
- **Grader (deep):** intake → `Channel<T>` queue → `BackgroundService` worker →
  per-criterion LLM judgment → evidence validation → deterministic composite →
  persisted result + audit trail. States: `queued → graded | needs_human_grading | failed`.
- **Generator (thin):** JD → one structured LLM call → draft weighted/anchored
  criteria → human approval → immutable versioned rubric. Weights normalised in code.
- Repository-layer `tenantId` scoping (auth stubbed).
- **PII / name-blind redaction of the transcript before it reaches the LLM.**
- **Minimal four-fifths (80%) adverse-impact check** over *seeded synthetic* data:
  selection rate per group + 80%-rule flag. Mechanism only.
- xUnit + FluentAssertions tests for the §3 invariants.
- `ReplayClaudeClient` is the README default — clean clone runs with ZERO credentials.

**Design-doc-only (NOT built):**
- The candidate/admin frontend and the human hire-decision UI/step (the rubric
  *approval* endpoint that gates the generator IS built; the rich review UI is not).
- The full LL144 / EU-AI-Act adverse-impact *audit report* (production runs it over
  real aggregate population data we don't have in a demo — document this explicitly).
- Real auth/identity; real video/biometric processing.

**Red flags to avoid (named by the grader):**
- Over-engineering / six-service architecture (or a heavy job framework) for one slice.
- Sanitized prompt transcripts.
- Code I can't explain. **Never write code I haven't reasoned through with me.**

**⚠️ Hard guardrail:** Do NOT process real video or biometric data. Input is a text
transcript stub only.

## 5. Code & testing conventions

- **C# 12 / .NET 8.** Nullable reference types ON; treat warnings seriously.
- **Records for the domain contract**; immutability by default.
- **Validate at boundaries** (LLM output, API input). Trust internal calls.
- **No premature abstraction.** Build for this slice, not hypothetical futures.
- **Pure core, I/O at the edges.** `Validate`, `Aggregate`, `Redact`, generator
  `Normalize`, and the four-fifths math are **pure** (no DB/LLM/HTTP) and unit-tested
  in isolation. This is where most tests live.
- **Adapter discipline.** Only the Claude adapter touches `HttpClient`/the Anthropic
  API. Everything else depends on `ILlmClient`.
- **Tests prove the invariants.** Each §3 constraint has at least one test that fails
  loudly if the invariant breaks (hallucination rejection, abstention, low-confidence
  routing, degradation, deterministic aggregation, tenant isolation, four-fifths).
- **Comments explain *why*, not *what*** — especially the confidence framing (§3.4)
  and "the LLM never computes the composite" (§3.1). Every stub names the constraint
  it enforces.
- **EF Core migrations** for schema changes; the audit trail is append-only by design.

## 6. AI workflow / build-log discipline

- `/ai` holds the build log and is itself graded:
  - `ai/prd-grader.md` — the spec for both modules.
  - `ai/prompts/session-log.md` — the real prompt history, dead-ends included.
  - `ai/decisions/` — course-corrections, one file each (0001 scope, 0002 the .NET switch).
- **Log as we go, not retroactively.** Record failed prompts and direction changes
  honestly — the messy middle is the point.
- When you make a non-trivial design call, state the rationale so I can capture and
  defend it live.

## 7. How to work with me

- Senior engineer (~12 yrs; ex-MS Azure data pipelines; built CashDesk solo). C# is my
  production backend language. Be terse and decisive. Diagnose + fix in one pass.
- **Align before building when it's load-bearing.** Docs (CLAUDE.md + PRD) precede
  code; the domain contract precedes the modules. Respect that ordering.
- When building N similar things, do ONE → verify → wait → next. Don't bulk-experiment.
- Don't take destructive or hard-to-reverse actions without asking.
