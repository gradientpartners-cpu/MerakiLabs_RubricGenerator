# /ai — AI Build Log

This folder is a graded deliverable. It is the honest record of how this repo was
planned and built with an AI IDE — including the wrong turns and recoveries.

| File | What it is |
|---|---|
| [`prd-grader.md`](prd-grader.md) | The spec fed to the tool: grader (deep) + generator (thin). |
| [`prompts/session-log.md`](prompts/session-log.md) | Ordered prompt history — the messy middle, dead-ends included. |
| [`decisions/`](decisions/) | Notable course-corrections, one file each, with rationale. |

The standing rules file is [`/CLAUDE.md`](../CLAUDE.md) — the contract that keeps the
AI from drifting off the architecture.

**Tooling:** Claude Code (Opus) in the terminal for building; `claude-sonnet-4-6`
behind a `replay|live` adapter for the product's own LLM calls.
