# Decision 0001 — Grader is the deep slice; generator stays thin

**Date:** 2026-06-13 · **Status:** accepted

## Context
Scope grew from "build the grader" to "build both the JD→rubric generator and the
grader." The risk: spreading effort across two modules and ending up with two shallow
ones — diluting the part that actually demonstrates senior judgment.

## Decision
- The **rubric grader** remains the **deep** slice: per-criterion constrained LLM
  judgment, boundary-validation anti-hallucination, deterministic weighted composite
  in code, abstention, graceful degradation, tenant isolation, full audit trail.
- The **JD→rubric generator** is built but **thin**: one structured LLM call → draft
  weighted/anchored criteria → human approval → immutable rubric. Weights normalised
  in code. No UI, no JD-parsing sophistication.
- **If effort must be cut, it is cut from the generator, never the grader.**

## Why
Grading a candidate is a **high-stakes output** — a wrong grade harms a real person —
so hallucination detection and auditability *there* is the strongest story. The
generator's output is **human-approved before use**, so its mistakes are caught by
that gate; depth spent there buys far less signal. Replacing the grader with the
generator would trade the strongest safeguard narrative for a weaker one.

## Consequences
- The generator reuses the grader's adapter, repository, and audit infrastructure —
  cheap to add, demonstrates the end-to-end pipeline (`JD → draft → approve → grade →
  audit`) without a second deep build.
- Encoded as a hard constraint in `CLAUDE.md` §9 so the AI won't over-invest in it.
