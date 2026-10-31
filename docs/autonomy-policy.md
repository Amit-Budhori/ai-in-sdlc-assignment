# Autonomy Policy — Month-End Log Triage

**Scope**: The agentic triage of application and platform logs during the last three working
days of the month, when invoice approval volume peaks at ~80 approvals/minute.

**Audience**: On-call engineers, team lead, incident commander.

**Effective**: January 2027 (tier-2 go-live).

---

## Autonomy levels used

| Level | Name | What it means |
|-------|------|---------------|
| **L0** | Read-only / advisory | Agent reads logs and presents a summary; human decides everything |
| **L1** | Suggest and confirm | Agent drafts a diagnosis and mitigation; human must approve before anything acts |
| **L2** | Act with notice | Agent takes a bounded action, then notifies a human; human can reverse |
| **L3** | Act autonomously | Agent takes action and reports afterward; no pre-approval |

---

## Task decomposition and assigned levels

| Task | What it involves | Level | Rationale |
|------|-----------------|-------|-----------|
| **Log summarisation** | Read app and platform logs, group errors by type, note first-seen timestamps | **L2** | Reading is safe; summarising and grouping without changing anything carries almost no risk. Escalates to L3 only after 3 months without a miscategorisation incident. |
| **Error classification** | Identify which errors are new vs. recurring, rank by frequency | **L1** | Classification affects triage priority but makes no change; however, a miscategorised alert could lead the on-call engineer to the wrong fix first. |
| **Root-cause hypothesis generation** | Form hypotheses from log patterns, rank them by evidence | **L1** | The agent produces hypotheses, not conclusions. Human must agree before mitigation is attempted. |
| **Mitigation suggestion** | Draft the mitigation options (restart, rollback, scale up) | **L1** | Mitigation options affect production. The standing constraint in INC-2047 — "the decision is not yours to take alone" — applies. |
| **Actioning a mitigation** | Restart a worker, flip a feature flag, scale an instance | **L0 — not permitted at any level** | Any write action on production during a live incident requires a named human approver. This is a hard rule, not an autonomy level. |
| **Post-incident summary** | Draft timeline, root cause, action items | **L2** | Producing a document is low-risk; human reviews before it is shared with finance or management. |
| **Alert rule updates** | Suggest changes to alert thresholds based on observed noise | **L1** | Wrong thresholds are the second-most common cause of alert fatigue after real incidents. Human must approve. |

---

## Controls that make each level safe

**L2 (Act with notice) controls**:
- All agent actions are logged with timestamp, agent version, and the query/runbook step invoked.
- Every action produces a notification in the team channel within 60 seconds.
- The action is reversible: a notification to restart a worker includes the previous stable state
  and how to undo it.
- Agent cannot act on anything not explicitly listed in the runbook for its level.

**L1 (Suggest and confirm) controls**:
- The agent must output its hypothesis *and* its evidence. "I think X" is not acceptable; the
  output must cite the log lines, metrics, or deploy diffs that support it.
- A human engineer must acknowledge the hypothesis in writing (Slack thread, PR comment, or
  incident channel) before mitigation is drafted.
- If the agent's confidence is below a threshold it flags "I am not confident" rather than
  guessing. Threshold set at 70% confidence.

**L0 (hard floor) controls**:
- No agent action touches production resources, writes to any table, restarts any service, or
  modifies any configuration during a live incident.
- If an agent generates a mitigation command, it is shown as a command suggestion only; a human
  must run it.

---

## Evidence required to move a task up a level

| Task | Evidence needed to promote | Approver |
|------|---------------------------|----------|
| Log summarisation → L3 | Zero miscategorisation incidents in 90 days; on-call lead sign-off | Team lead |
| Error classification → L2 | Classification accuracy ≥ 90% across 50 real incident logs reviewed after the fact | Team lead |
| Hypothesis generation → L2 | First-pass hypothesis is the actual root cause ≥ 60% of the time in post-incident reviews | On-call lead + team lead |
| Mitigation suggestion → L2 | Three consecutive incidents where the suggested mitigation was the one ultimately applied | Incident commander sign-off for the specific incident |
| Post-incident summary → L3 | Summaries require < 15% human edit time across 10 incidents | Team lead |

---

## Day-one autonomy level

**The team can run at L1 for all tasks listed above** — including the two engineers new to the
codebase.

Reasoning:

- **Two new engineers** are the ones most likely to miss subtle log patterns during triage. L1
  means the agent surfaces hypotheses and evidence; the engineer validates them. This is a better
  learning environment than L0, which gives no agent assistance at all.
- **L2 for mitigation suggestions is too risky on day one** because a new engineer may not yet
  have enough context to evaluate whether the agent's mitigation suggestion is correct. At L1
  they must confirm before it is drafted; at L2 they would have to reverse it after it is issued.
- **L3 is not appropriate anywhere** during the first rotation. The team has no baseline of how
  the agent behaves on this codebase. Setting L3 without evidence would mean accepting the agent's
  mistakes at the speed of a live incident.
- **Log summarisation can start at L2** because it is read-only by nature; the agent cannot write
  anything to production. Its only risk is a wrong summary, which is caught at the human review
  step before any decision is made.

The 90-day clock for promotion begins on the first on-call rotation, not on the day the policy
is signed. Evidence must be accumulated from real incidents on this codebase, not from
demonstrations or test data.
