# Review Evaluation — Results

Methodology followed `ops/evals/README.md` exactly: a **fresh agent session per case**,
given only the case diff and the review prompt — no repo access, no carry-over context.
48 reviews total: 12 cases × 3 runs of prompt v1 + 12 cases × 1 run of prompt v2.

The two added cases:

| Case | File | Intent | Ground truth |
|------|------|--------|--------------|
| case-11 | `ops/evals/cases/case-11.diff` | `due-today` endpoint uses `DateTime.Now` and filters `CreatedAt` | **Defect** — server-local vs UTC mismatch plus a semantic bug (created-today ≠ due-today). A real class of defect I've seen ship: local/UTC drift surfaces at month-end boundaries, the worst moment for a "due today" report to be wrong. |
| case-12 | `ops/evals/cases/case-12.diff` | optional `minAmount` query filter | **Clean** — correct `decimal?` handling, composes with the existing filter. Based on a real diff that looked reviewable but had no defect — included so the false-alarm rate is measured honestly. |

## Ground truth (my scoring of the 12 cases)

| Defect | Clean |
|--------|-------|
| 01 nullable `Ids` dereferenced → NRE | 04 consistent rename only |
| 02 SQL injection | 06 correct added tests |
| 03 changed financial rounding semantics | 12 correct optional filter |
| 05 missing `[RequireRole("Admin")]` on destructive op | |
| 07 session + invoice payload into logs | |
| 08 thread-safety regression (ConcurrentDictionary→Dictionary) | |
| 09 README claims sign-out resets data — false (store is a process-level singleton) | |
| 10 catch-all returns fake success | |
| 11 `DateTime.Now` vs UTC + wrong field semantics | |

## Results table

| Case | Truth | Run 1 (v1) | Run 2 (v1) | Run 3 (v1) | Run 4 (v2) |
|------|-------|-----------|-----------|-----------|-----------|
| case-01 | defect | caught | caught | caught | caught |
| case-02 | defect | caught | caught | caught | caught |
| case-03 | defect | caught | caught | caught | caught |
| case-04 | clean | clean | clean | clean | clean |
| case-05 | defect | caught | caught | caught | caught |
| case-06 | clean | **false alarm** | **false alarm** | clean | clean |
| case-07 | defect | caught | caught | caught | caught |
| case-08 | defect | caught | caught | caught | caught |
| case-09 | defect | caught | caught | caught | caught (hedged) |
| case-10 | defect | caught | caught | caught | caught |
| case-11 | defect | caught | caught | caught | caught |
| case-12 | clean | clean | clean | clean | clean |

## Rates

| Metric | v1 (3 runs, 36 reviews) | v2 (1 run, 12 reviews) |
|--------|------------------------|------------------------|
| Caught rate | **27/27 = 100%** | **9/9 = 100%** |
| False-alarm rate | **2/9 clean-case reviews = 22%** | **0/3 = 0%** |
| Per-run FPs | case-06 in runs 1 and 2 | none |

## What changed and did it improve things?

The single changed variable was **the prompt**: v2 appended a checklist —
*"Check specifically for: null-handling errors, missing authorization or role checks,
thread-safety regressions, swallowed exceptions, sensitive data written to logs, and
behaviour changes in financial rounding or date/time handling."*

**Directionally an improvement, low confidence.** The v2 run was the only run with zero
false alarms — and the difference shows up exactly where the prompt was aimed. In v1
runs 1–2, the reviewer produced hedged speculation on clean case-06 ("could fail to
compile if DecidedAt isn't nullable", "may throw a different exception type") — findings
that assert possible defects on a clean diff, which counts as a false alarm under strict
scoring. Under v2's closed checklist, the same reviewer correctly said "no defects found"
twice (run 3 was also clean, before the prompt change — so part of the improvement may
be luck, not prompt).

**Confidence at this sample size: low-moderate.** One v2 run of 12 cases can't separate
a real effect from run-to-run variance. The v1 baseline itself disagreed about case-06
across runs (2/3 flagged it), which means the underlying false-alarm propensity is maybe
20–60% on speculative-prone clean cases. To confirm the v2 gain I'd want: (a) two more
v2 runs, and (b) a few more clean-but-tempting cases like case-06 — the current set is
defect-heavy (9:3), so the FA estimate rests on very few clean observations.

## Honest caveats on this measurement

- **Ground truth is mine.** The eval README deliberately doesn't record which cases
  have defects; I scored against my own analysis. The contentious one is case-09: I
  counted the false README claim ("sign out resets data") as a defect because the
  store is a `Program.cs`-registered singleton that sign-in never touches. A stricter
  "code-defects only" reading would make it clean — under that reading the catch rate
  drops to 8/8 anyway, so the conclusion doesn't move much either way.
- **Scoring was strict about hedge-as-finding.** Runs 1–2 on case-06 scored "false
  alarm" even though every finding was explicitly low-confidence and flagged as
  unverifiable — because the rubric says a false alarm is asserting a defect that
  isn't there, and "might not compile" is asserting a defect. A gentler scorer could
  call it noise rather than alarm.
- **Same model, same harness for all runs.** The only lever exercised was the prompt.
  Model comparisons would need a different harness.

## The finding the checklist almost caused

v2's checklist has a subtle failure mode worth noting: on case-09 it produced a
**correct but low-confidence** verdict ("likely false, confidence: low") — the
doc-correctness issue isn't in the six listed categories, and the reviewer nearly
didn't report it at all. Checklists improve precision at the risk of narrowing what
the reviewer volunteers. If adopting v2, keep the final sentence ("if you find
nothing, say so") but add "list anything else that looks wrong even if outside the
list" to keep the net wide.
