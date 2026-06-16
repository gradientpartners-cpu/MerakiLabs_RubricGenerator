# Decision 0005 — A thin demo console, deliberately separate from the product frontend

**Date:** 2026-06-16 · **Status:** accepted · **Relates to:** `demo/index.html`, `RubricGrader/Program.cs` (CORS)

## Context
For the live presentation I wanted the existing API to be *clickable* — health → generate →
approve → grade → audit → fairness, in order, with the caught hallucination visible — without
asking a reviewer to paste curl commands. The candidate/admin **product frontend is explicitly
design-doc-only** (CLAUDE.md §4); building a real SPA would be scope creep and edge toward the
over-engineering red flag the brief names. So the bar was: make the API demonstrable with the
**least possible new surface area**, and keep it unmistakably separate from the unbuilt product UI.

## Decision
A **single static file**, [`demo/index.html`](../../demo/index.html): inline CSS, vanilla JS,
no framework, no build step, no npm, no dependencies. It is opened directly in a browser (or
served statically) and calls the existing endpoints at `http://localhost:5026` — zero new
backend behavior. Six sequential panels, each gated on the previous one's output:

1. **Health** — `GET /health`.
2. **Generate** — `POST /jd`; renders the draft criteria and highlights the
   `youthful_energy`/`age` **fairness flag** (flagged, never auto-dropped). Captures the rubric id.
3. **Approve** — `POST /rubrics/{id}/approve` (with `X-User-Id`); shows the frozen Approved version.
4. **Grade** — `POST /evaluations` against the pre-seeded `rub-backend-screen-v3` with the
   built-in transcript; captures the evaluation id.
5. **Audit** — `GET /applications/{id}/audit`; per-criterion table with the **EvidenceNotFound**
   row in red, score dropped, composite **withheld (null)**, state `NeedsHumanGrading`.
6. **Fairness** — `GET /fairness/adverse-impact`; per-group table, `group_c` flagged in red.

Every request carries `X-Tenant-Id: tenant-acme`. Cross-step state (rubric id, eval id) lives in
plain JS variables — no localStorage/sessionStorage. Each panel shows a human-readable rendering
plus a collapsible raw-JSON view; later buttons stay disabled until their prerequisite has run.

## The one backend change (flagged)
The static page's origin differs from the API's, so the browser requires an
`Access-Control-Allow-Origin` header. The **only** backend change is a **demo-only CORS policy**
in `Program.cs` (`AllowAnyOrigin/Header/Method`, applied via `app.UseCors("demo")`). It is safe
here because the API uses **no cookies/credentials** — the tenant is a plain header — so any-origin
exposes nothing a caller couldn't already send. Production auth would scope this to known origins.
No endpoint, schema, pipeline, or test was touched.

## What this is NOT
- **Not the product frontend.** No candidate/admin views, no auth UI, no hire-decision step — those
  stay design-doc-only (DESIGN.md). This is a presentation aid that proves the *backend* is real.
- **Not on the demo's critical path.** The zero-credential `dotnet test` (76 green) and the curl
  walkthrough in the README remain the source-of-truth demonstrations; the console is a convenience
  layer over the same endpoints.

## Consequences
- Reviewers can exercise the full pipeline in a browser in ~30 seconds, with the caught
  hallucination and the fairness flag both visible.
- One additive file + one guarded CORS policy. No shipped grading behavior, no schema, no existing
  test changed; the fresh-clone `dotnet test` is untouched by construction.
- Verified end-to-end against the live server: all six endpoints return the expected chained
  shapes, the ownership criterion is rejected `EvidenceNotFound` with a null composite, and
  `group_c` is flagged by the four-fifths check.
