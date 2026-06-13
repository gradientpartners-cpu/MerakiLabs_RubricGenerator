# Decision 0002 — Switch the build from Python/FastAPI to C#/.NET

**Date:** 2026-06-13 · **Status:** accepted · **Supersedes:** the Python stack in 0001-era docs

## Context
We scaffolded this slice in Python/FastAPI. Partway through I caught a mismatch in my
own reflex: **C#/.NET is my actual production backend language.** At Microsoft and in
CashDesk, C# owns the application/backend layer; Python was only ever the **ML
sidecar**. This slice has **no ML in it** — it's backend orchestration: async jobs, an
LLM call behind an adapter, evidence validation, deterministic aggregation,
multi-tenant data access. There is no reason to be in Python here.

## Decision
Rebuild the skeleton on **.NET 8 / ASP.NET Core**, preserving every locked
architectural constraint unchanged. Python is dropped from this repo entirely.

## Why
- **Defensibility under hard questioning.** "Code you can't explain" is a named eval
  red flag, and they push hardest in the live presentation. Building in the language I
  defend most fluently is the right call.
- **It mirrors my real architecture.** C# owns the application; Python is reserved for
  ML. Knowing *when not to reach for Python* is itself the judgment I want to show.
- **No ML in this slice.** The thing Python would have bought me (ML ecosystem) isn't
  needed; the orchestration/typing/async story is at least as strong in .NET.

## Trade-off considered (honestly)
Venture studios often lean Python for AI work, so .NET is a slightly less expected
choice for an "AI hiring" trial. I weighed that stylistic signal against
**defensibility** and **honest fit**, and defensibility wins: a slice I can explain
line-by-line in my strongest language beats a fashionable-but-shakier one. The switch
itself — catching my own reflex and correcting it — is a build-log asset, not
something to hide.

## Consequences
- 1:1 stack remap (see CLAUDE.md §2): FastAPI→ASP.NET Core minimal API; Redis/ARQ→
  `BackgroundService` + `Channel<T>` in-process queue (no Hangfire unless durability
  genuinely earns it — flag, don't decide silently); Pydantic→C# records; Claude SDK→
  `HttpClient` behind `ILlmClient` (Live/Replay); EF Core + Npgsql; xUnit +
  FluentAssertions.
- All §3 constraints (LLM judges, code aggregates; evidence existence-check;
  abstention; degradation; redaction; four-fifths; tenant isolation) carry over
  unchanged.
- Replay-fixture backend stays the README default: clean clone runs with zero
  credentials.
