# AI Augmentation Log

How the agent (Devin, SWE-2) was used on this assignment, and what was verified
by hand versus taken on trust. Listed in order of the work.

## Task 1 — As-built and target diagrams

- **Agent did**: read the codebase (controllers, domain, repository, auth middleware,
  Program.cs) and produced the Mermaid container + sequence diagrams, the citation
  table, and the inferred-items list.
- **Human verified**: every citation against the actual files. Two corrections were
  needed and made: (a) the agent's first pass described the repository store as
  `ConcurrentDictionary` — the actual `InMemoryInvoiceRepository` uses
  `List<Invoice>` behind a `lock` (`InMemoryInvoiceRepository.cs`); (b) `Program.cs`
  registers the repo as a singleton, which matters for the case-09 eval analysis.
- **Human decided**: the boundary between "as-built" and "target" — what is marked
  `[NOT YET BUILT]` — was set by cross-reading `ops/README.md` against the `.csproj`
  files (no EF Core, no HttpClient, no Service Bus SDK), not from the agent's claim.

## Task 2 — Architecture test

- **Agent did**: chose NetArchTest.Rules, added the package + project reference,
  wrote `ArchitectureTests.cs`, ran `dotnet test`.
- **Human verified**: the test is not vacuous — first run failed for a real reason
  (record types in the Controllers namespace counted as classes with many methods),
  fixed by filtering to `ControllerBase` subclasses. A `Domain_Assembly_ContainsTypes`
  sanity check was added so an empty type list can't produce a silent pass.
- **Run**: `dotnet test backend/InvoiceApp.sln` → 11 tests, all green.

## Task 3 — ADR-001 exactly-once

- **Agent did**: laid out the three options (idempotency key + dedupe, transactional
  outbox, optimistic + reconcile) with trade-offs.
- **Human stress-tested** (all three challenges required by the brief):
  1. *Preference nudge toward Option A* — the agent moved its recommendation without
     new information. This was the strongest evidence for the *wrong or useless*
     section: the agent optimises for agreeableness unless explicitly pinned.
  2. *3 a.m. month-end failure* — the agent described the crash-before-ack case
     correctly but missed the monitoring gap (backed-up outbox during PayEx
     throttling has no alert path). Added to the ADR.
  3. *Cost* — "two to three sprints" was useless precision; the 7–10 day build
     estimate in the ADR comes from listing work items by hand.
- **Human wrote**: the decision, the revisit triggers, the PayEx idempotency-key
  blocker question, and the *Where the agent was wrong or useless* section.

## Task 4 — Autonomy policy

- **Agent did**: drafted the level decomposition for month-end log triage.
- **Human verified/edited**: anchored the L0 hard floor to the INC-2047 standing
  constraint ("the decision is not yours to take alone"), set day-one to L1 on the
  basis of the two new engineers, and set the evidence thresholds (60% first-pass
  accuracy, 90-day windows) myself — the agent's initial thresholds were round
  numbers with no rationale.

## Task 5 — Review evaluation

- **Agent did**: ran 48 reviews — each case in a **fresh subagent session** given
  only the diff + the review prompt, matching the eval README's methodology
  ("start a fresh session for each case"). Runs 1–3 used prompt v1; run 4 used the
  checklist prompt v2 (the one changed variable).
- **Agent wrote**: case-11 (UTC/local drift, a defect) and case-12 (clean filter) —
  both based on defect patterns observed on real services.
- **Human scored**: every verdict against the ground-truth table using the strict
  rubric (hedged speculation on a clean case = false alarm). The honest result —
  27/27 caught on v1, 2/9 false alarms, case-06 flipping between runs — is recorded
  as measured, not smoothed.

## Task 6 — Risk audit + measurement plan

- **Agent did**: mapped `.mcp.json` + `AGENTS.md` against the lethal trifecta;
  identified that Playwright MCP (`@latest`, unpinned) is both the untrusted-content
  intake and an external-action channel.
- **Human decided**: remove the untrusted-content leg (drop the MCP server, ban
  `@latest`) rather than accept the trifecta — the tool isn't needed for the work.
- **Agent computed**: the two capturable baseline numbers from
  `ops/pipeline/run-timings.csv` with awk — CFR = 2/20 = **10%**, median pipeline
  = **1,875 s ≈ 31.3 min** — and verified by recounting failure rows (runs 6, 14).
- **Human wrote**: which baselines are *not* capturable today (review turnaround
  needs PR metadata that doesn't exist in this repo) rather than letting the agent
  substitute a proxy silently.

## What the agent was measurably good at

- Volume work: 48 independent eval reviews, each correctly scoped to a single diff.
- Finding the real defects: 100% catch rate across all runs on the 9 defect cases.
- Drafting structure: ADR options table, policy level table, diagram skeletons.

## What required human correction or refusal to accept

1. Agreeableness under preference pressure (ADR stress test #1).
2. Missing operational blind spots (outbox back-pressure alerting; the PayEx
   idempotency-key question never surfaced unprompted).
3. False precision ("two to three sprints") instead of decomposed estimates.
4. Speculative findings on clean diffs — the measured 22% false-alarm rate on v1
   is exactly the failure mode the eval set exists to quantify.
5. One factual error caught on verification: described the repository store as
   `ConcurrentDictionary`; it is a locked `List<Invoice>`.

## Model / environment

- Agent: Devin (SWE-2 High), Devin Desktop CLI.
- Eval reviewers: fresh `subagent_explore` sessions, same underlying model, no repo
  access — the diffs were embedded in the task prompt so context from the codebase
  could not leak into the review.
- Date of work: 2026-10-31.
