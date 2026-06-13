# Audit trace — evaluation `eval-2f9c`

This is a human-readable rendering of the response from
`GET /applications/eval-2f9c/audit`. The machine-readable source is
[`audit-trace-example.json`](audit-trace-example.json). It reconstructs one grading
decision end to end: the outcome, every criterion, the **exact quote** the model cited,
whether that quote was **verified against the transcript**, and the **rubric version**
that defined the scoring — so the decision can be followed without reading any code.

---

## Outcome

| | |
|---|---|
| **Result** | **Needs human review** |
| **Composite score** | **None produced** |
| Tenant | `tenant-acme` |
| Candidate artifact | `artifact-7731` |
| Model | `claude-sonnet-4-6` (replayed from recorded fixtures) |
| Grading prompt version | `grade-v1` |
| Rubric | **Backend Engineer Screen, version 3** (`rub-backend-screen-v3`, status: Approved) |

**Why no score?** Three criteria were assessed. Two were supported by quotes that were
verified word-for-word in the interview transcript. The third cited a quote that could
**not be found** in the transcript — the system caught the unverifiable citation, refused
to use it, and therefore did **not** compute a final number. The evaluation was routed to
a human instead of guessing. *No score is ever invented to fill the gap.*

> **"Needs human review" is not a rejection of the candidate.** The two verified criteria
> scored *well* (4/5 communication, 5/5 technical depth). The system is simply declining to
> *auto-finalize* a result when one criterion's supporting quote can't be verified. A human
> reviewer now completes that one criterion and the assessment proceeds. "No composite"
> means "not yet finalized by a person," **not** "the candidate failed."

> **What harm this prevented.** Had the unverifiable 5/5 been trusted, the candidate would
> have received an inflated ownership score for an accomplishment they never claimed — a
> correctness failure on its own. At population scale, if such fabrications skew by group,
> they become an **adverse-impact / fairness** problem (the disparity our adverse-impact
> analysis is designed to detect). Refusing to score on unverifiable evidence keeps a
> single bad citation from quietly becoming a systemic bias.

---

## The interview transcript (the evidence of record)

Each line has a turn id. Every quote below is checked against the text of the specific
turn it cites — not against the transcript as a whole.

| Turn | Speaker | What was said |
|---|---|---|
| t1 | Interviewer | Walk me through a system you owned end to end. |
| t2 | Candidate | I owned our billing pipeline for two years, from ingestion to invoicing. |
| t3 | Interviewer | How did you handle a major incident? |
| t4 | Candidate | During a Postgres failover I coordinated the rollback and wrote the postmortem myself. |
| t5 | Interviewer | Tell me about scaling. |
| t6 | Candidate | We moved to a sharded cluster and cut p99 latency from 800ms to 300ms. |

---

## The decision, criterion by criterion

### 1. Clarity of communication — weight 30%
- **Score:** 4 / 5 · confidence: high
- **Cited quote:** *"from ingestion to invoicing"*
- **Cited turn:** t2
- **Verification:** ✅ **Valid** — the quote appears verbatim in turn t2.
- **Counts toward the score?** Yes.

### 2. Technical depth — weight 40%
- **Score:** 5 / 5 · confidence: high
- **Cited quote:** *"cut p99 latency from 800ms to 300ms"*
- **Cited turn:** t6
- **Verification:** ✅ **Valid** — the quote appears verbatim in turn t6.
- **Counts toward the score?** Yes.

### 3. Ownership & accountability — weight 30%
- **Proposed score:** ~~5 / 5~~ · **discarded**
- **Cited quote:** *"I personally rebuilt the entire authentication system"*
- **Cited turn:** t4
- **Verification:** ❌ **Evidence not found** — this sentence does **not** appear in turn t4
  (or anywhere in the transcript). The candidate's turn t4 was about a Postgres failover
  rollback and postmortem, not authentication. (The system reports only what it can prove:
  the citation is *not verifiable* against the transcript — it makes no claim about *why*
  the quote was produced.)
- **Counts toward the score?** **No.** The proposed score was dropped; the criterion was
  flagged for a human.

---

## What a reviewer sees and does

1. Two of three criteria are backed by verifiable evidence (communication, technical depth).
2. One criterion (ownership) was flagged because the supporting quote could not be found
   in the cited turn — **evidence not found**, recorded rather than hidden.
3. Because a required criterion has no trustworthy score, **no composite was computed**.
   A human grader now reviews the ownership criterion directly.

Every element above — the proposed score, the exact cited quote, the turn it claimed to
come from, and the pass/fail of verification — is preserved in the append-only audit
record and tied to rubric version 3. Nothing is paraphrased, summarized, or inferred.
