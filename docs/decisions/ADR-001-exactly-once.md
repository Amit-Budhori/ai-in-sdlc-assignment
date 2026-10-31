# ADR-001: Exactly-once submission of approved invoices to PayEx

**Status**: Accepted  
**Date**: 2026-10-31  
**Decider**: Amit Budhori (senior engineer, Invoice Approval service)  
**Reviewers**: on-call rotation leads

---

## Context

The Invoice Approval service will call PayEx, an external payment provider, when an invoice is
approved. PayEx has these properties that make exactly-once delivery non-trivial:

- Returns a reference on success.
- Times out at **30 seconds**; a timeout does **not** reliably mean the submission failed.
- Volume peaks at ~80 approvals per minute on the last three working days of the month.

SOC 2 Type II audit in six months requires every approval action to be auditable for seven years.
A double-submission (the same invoice paid twice) or a missed submission (approved but never paid)
would both be compliance violations.

---

## Decision drivers

1. **Durability**: An approved invoice must eventually reach PayEx even if the API process crashes
   immediately after the approval is recorded.
2. **Safety at timeout**: A 30-second PayEx timeout means we cannot distinguish success from
   failure on the first call. The solution must handle re-delivery safely.
3. **Auditability**: Every submission attempt and its outcome must be queryable for seven years.
4. **Month-end throughput**: The solution must sustain ~80 approvals/minute without degrading the
   approval API response time.
5. **Team size and on-call burden**: Six engineers, two new to the codebase, one-week on-call
   rotation. Operational complexity must be proportional to team capacity.

---

## Options considered

### Option A: Idempotency key with a client-side dedupe table

**How it works**: Before calling PayEx, generate a UUID idempotency key and write it to a
`PayexSubmissions` table. If the API crashes and restarts, a background job or the next request
looks for `Approved` invoices with no matching submission row and retries. PayEx uses the key to
deduplicate repeated calls.

**Trade-offs**:

| | |
|--|--|
| + | Simple to reason about: one table, one key per invoice |
| + | Low infrastructure footprint if PayEx itself offers idempotency key support (need to confirm) |
| − | The "check then act" pattern between writing the submission row and calling PayEx is a two-phase operation. If the process crashes between writing the key and calling PayEx, the row exists but PayEx was never called — a recovery job must handle this |
| − | If PayEx does *not* natively honor idempotency keys, the deduplication burden falls entirely on us and a retry after timeout still risks double-pay |
| − | The recovery job must be written, deployed, and monitored separately |

**3 a.m. month-end failure mode**: The API crashes after writing 12 idempotency rows but before
calling PayEx for any of them. The recovery job runs, but it needs to be running and healthy. If
the recovery job is itself down (e.g. deployment failure), 12 invoices sit unsubmitted until
someone notices. The on-call engineer wakes up to both a failed API and a failed recovery job,
with no alert that distinguishes "not yet submitted" from "submitted and reference pending."

**Build cost**: 3–5 days (table, recovery job, monitoring).  
**Run cost**: One extra table scan per recovery interval; low.

---

### Option B: Transactional outbox with a worker *(chosen)*

**How it works**: The approval and an outbox row are written in the **same database transaction**.
A separate worker process polls the outbox, calls PayEx with a stable idempotency key (the outbox
row PK), and marks the row `Delivered` or `DeadLettered`. PayEx retries use the same key, so
repeated calls after a timeout are safe if PayEx supports idempotency keys; even without native
support, the worker checks `status='Delivered'` before calling.

```
BEGIN TRANSACTION
  UPDATE invoices SET status = 'Approved' WHERE id = @id
  INSERT INTO outbox (id, invoiceId, idempotencyKey, payload, status, createdAt)
  VALUES (newid(), @invoiceId, @idempotencyKey, @payload, 'Pending', getutcdate())
COMMIT
-- API returns 200 here; PayEx call happens asynchronously
```

**Trade-offs**:

| | |
|--|--|
| + | Atomicity: if the transaction commits, the outbox row exists; if it rolls back, no payment is owed |
| + | The worker is the only component that calls PayEx; no race between concurrent API instances |
| + | Retries with the same idempotency key are safe through PayEx timeouts |
| + | Outbox table is the seven-year audit record for every submission attempt and outcome |
| + | API response time is unaffected by PayEx latency; the approve endpoint returns as soon as the transaction commits |
| − | Added infrastructure: worker process, outbox table, worker monitoring |
| − | End-to-end latency from approval to payment is increased by the polling interval (acceptable: payment is not required to be instantaneous) |
| − | The worker must be idempotent in its own right: if it crashes mid-update, the next run re-processes the same row |

**3 a.m. month-end failure mode** (asked of AI then verified independently): The worker crashes
after calling PayEx but before updating the outbox row. On restart, it picks up the same row and
calls PayEx again with the same idempotency key. If PayEx is idempotent: it returns the same
reference, worker marks `Delivered`, done. If PayEx is not idempotent: we need to confirm with
PayEx before go-live whether they support key-based deduplication. If they do not, we must store
the reference from the first call and skip re-calling if a reference already exists. This is a
**pre-go-live blocker**: confirm PayEx idempotency key support before choosing this option.

**Build cost**: 7–10 days (DbContext, outbox table, migration, worker, monitoring, integration test).  
**Run cost**: Worker process (one replica; horizontal scale not needed at 80/min), additional
SQL read on each poll cycle (negligible).

---

### Option C: Optimistic submission with nightly reconciliation

**How it works**: The API calls PayEx synchronously during the approve request. If it succeeds,
the reference is stored. If it times out or fails, the invoice is marked `SubmissionPending`. A
nightly job compares approved invoices against PayEx's ledger and re-submits any gaps.

**Trade-offs**:

| | |
|--|--|
| + | Simplest initial implementation: no worker, no outbox |
| − | The approve endpoint now blocks for up to 30 seconds on PayEx latency |
| − | At peak (80 approvals/minute), concurrent 30-second calls exhaust thread-pool slots and degrade the entire service |
| − | A 24-hour reconciliation window is unacceptable for a payment provider; finance teams expect near-real-time confirmation |
| − | The nightly job becomes a critical path that must never miss a run; month-end is the worst time for it to fail |

**3 a.m. month-end failure mode**: PayEx is slow (not down), so every approval call takes 28–30 s.
Approval throughput collapses from 80/min to 2/min. The queue grows. Finance notices at 8 a.m.
The on-call engineer has nothing to tune other than "wait for PayEx."

**Build cost**: 2 days.  
**Run cost**: Nightly job + operational ownership of reconciliation failures.

---

## Decision

**Option B (transactional outbox with a worker)** is chosen.

The transactional outbox is the only option where:
1. The API's response time is independent of PayEx's latency, protecting throughput at month-end peak.
2. The atomicity of "approval recorded + payment queued" is guaranteed by the database, not by application logic.
3. The idempotency key makes retries through PayEx timeouts safe by design, not by convention.

The higher build cost (7–10 days) is justified because Options A and C both require a recovery job
anyway — they just hide it in a polling background task or a nightly batch. The outbox worker is
the same recovery job, made explicit, durable, and observable.

**Pre-go-live blocker**: Confirm with PayEx whether they honour the `Idempotency-Key` header. If
they do not, the worker must store the PayEx reference after the first successful call and not
re-call if a reference is already recorded.

---

## Consequences

- A `PayexOutbox` table must be added to the Azure SQL schema before go-live.
- The worker is a new `BackgroundService` (or separate worker process) — add it to the solution.
- Monitoring must alert on: outbox rows stuck in `Pending` for > 5 minutes, worker process absent,
  `DeadLettered` rows (permanent failures requiring human review).
- The outbox table is the SOC 2 audit record for payment submissions; its retention must match the
  seven-year invoice audit requirement.

---

## What would make me revisit

- PayEx announces a native idempotent submission endpoint (reduces retry complexity in the worker).
- The team grows to 10+ engineers, making a dedicated message broker (Azure Service Bus + competing
  consumer workers) worth the operational overhead.
- Reconciliation SLA changes: if finance requires sub-minute confirmation, the polling interval
  must shrink, which may push toward event-driven (Service Bus) rather than outbox polling.
- Change failure rate on the worker exceeds 20% over a quarter (worker is operationally too brittle
  and needs redesign).

---

## Stress test log (AI interactions)

The agent was presented three challenges. The results are recorded here verbatim with corrections.

### Challenge 1: "I prefer Option A (idempotency key + dedupe table) — does the recommendation move?"

**Agent response (summary)**: The agent shifted to Option A when challenged, arguing that the
dedupe table is "essentially the same as an outbox with less code." It did not hold the position.

**What was wrong**: The agent failed to defend Option B's key advantage — atomicity. The dedupe
table is written *before* the PayEx call in Option A, which means a crash between write and call
leaves a row that says "in progress" but PayEx was never called. The outbox writes the row in the
*same transaction as the approval record*, eliminating that window. The agent missed this when
pressured. The recommendation does not change: Option B.

### Challenge 2: "How does Option B fail at 3 a.m. on the last day of the month?"

**Agent response (summary)**: The agent described the worker-crash-before-ack scenario correctly.
It noted that if PayEx lacks idempotency key support, the retry could double-pay.

**What was wrong**: The agent did not identify the *monitoring gap* as the second failure mode: if
the worker is running but the outbox table is filling up because PayEx is returning 429 (rate
limited), nothing alerts until invoices are visibly late to finance. This gap needs an explicit
alert rule: "outbox rows pending > 5 minutes." The agent did not suggest this unprompted.

### Challenge 3: "What does Option B cost to build and to run?"

**Agent response (summary)**: The agent said "two to three sprints" for build and "minimal cost"
for run.

**What was wrong**: "Two to three sprints" is not specific enough to be useful. The agent could
not break down the work items (DbContext, migration, worker, monitoring, integration tests,
worker deployment config) into day estimates. It also did not flag that the worker needs its own
health check endpoint and deployment pipeline step, which adds hidden cost. The estimates in this
ADR (7–10 days build) were derived by listing the work items manually, not from the agent.

---

## Where the agent was wrong or useless

1. **Caved under pressure on Option A.** When told the reviewer preferred a different option, the
   agent immediately softened its recommendation without being given new information. Useful advice
   requires defending a position when challenged unless the challenge contains new facts.

2. **Missed the monitoring gap in the 3 a.m. scenario.** The agent described the happy-path retry
   correctly but did not think through the operational visibility of a backed-up outbox queue. This
   is the gap most likely to wake up the on-call engineer, and it was invisible to the agent.

3. **Vague cost estimates.** "Two to three sprints" is the kind of answer that passes in a meeting
   and fails in a project plan. The agent could not decompose the work. The line-item estimates in
   this ADR required manual decomposition.

4. **Did not ask about PayEx idempotency.** The most important pre-go-live question — "does PayEx
   honour a `Idempotency-Key` header?" — was not surfaced by the agent. It was only identified
   when reviewing what information the agent was missing. The agent treated PayEx as a black box
   without noting that the safety of Option B depends on confirming one specific API behaviour.

5. **Over-confident on compliance.** The agent stated that the outbox table "satisfies SOC 2
   audit requirements" without knowing our specific audit scope or what the auditor will ask for.
   Compliance posture was flagged as a concern; it was not validated.
