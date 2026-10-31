# Measurement Plan — Baseline Before the January Rotation

Captures the delivery-performance baseline before the 24x7 rotation starts, so that
post-go-live changes (PayEx integration, persistence, on-call load) can be measured
against something real rather than vibes.

## Metrics, sources, and exact queries

| Metric | Definition | Exact source / query |
|--------|-----------|----------------------|
| **Lead time for changes** | Median time from PR opened to merged on `main` | GitHub API: `GET /repos/{owner}/{repo}/pulls?state=closed`, per PR compute `merged_at - created_at`. Repo-local proxy: time from a branch's first CI run to its first *successful* run in `ops/pipeline/run-timings.csv`. |
| **Change failure rate** | % of pipeline runs that end `failure` | `ops/pipeline/run-timings.csv`: `awk -F, 'NR>1 {r[$1]=$5} END {f=0;n=0; for(k in r){n++; if(r[k]=="failure")f++} print f/n}'`. In production: `GET /repos/{owner}/{repo}/actions/runs?status=failure` ÷ total runs. |
| **Review turnaround** | Median time from PR opened to first human review comment/approval | GitHub API: `GET /repos/{owner}/{repo}/pulls/{n}/reviews` and `/issues/{n}/comments`, earliest non-author timestamp − PR `created_at`. Not available in this repo — no PR metadata is stored in git history. |
| **Pipeline duration** (supporting) | Median wall time of a full CI run | Sum `duration_seconds` per `run_number` in `ops/pipeline/run-timings.csv`; median of the per-run totals. |
| **Deployment frequency** (supporting) | `push` events to `main` per week | Count of `event=push` rows ÷ weeks covered in `run-timings.csv`. |

## Baseline captured today

Two numbers are capturable from the repository as it stands. Both were computed
from `ops/pipeline/run-timings.csv` (20 CI runs, 2026-01-19 → 2026-02-02):

**1. Change failure rate = 10%** (2 failures / 20 runs)

| Run | Branch | Event | Conclusion |
|-----|--------|-------|------------|
| 6 | fix/notification-filter | pull_request | failure |
| 14 | fix/approval-audit | pull_request | failure |

Query used:
```bash
awk -F, 'NR>1 {r[$1]=$5} END {f=0;n=0; for(k in r){n++; if(r[k]=="failure")f++} print n, f, f/n}' ops/pipeline/run-timings.csv
# → 20 2 0.1
```

**2. Median pipeline duration = 1,875 s ≈ 31.3 min** (range 1,702–2,108 s)

Query used:
```bash
awk -F, 'NR>1 {sum[$1]+=$7} END {n=0; for(r in sum){v[n++]=sum[r]} asort(v);
  print "median", (n%2)? v[(n+1)/2] : (v[n/2]+v[n/2+1])/2; print "min",v[1],"max",v[n]}' ops/pipeline/run-timings.csv
```

Supporting observation (not a headline metric): per-branch first-run → first-green
gaps show lead time is review-bound, not CI-bound. `fix/approval-audit` ran CI at
Jan 29 23:20 and only went green at Feb 1 02:53 (~51.6 h), `chore/dependency-bumps`
took ~20 h — both dwarfs the 31-minute pipeline. The bottleneck is human review +
rework latency, which is exactly what "review turnaround" would confirm once GitHub
PR data is accessible.

## What cannot be captured from this repo today

- **Review turnaround**: git history contains no PR metadata (only merge commits);
  the repo has no PRs. Requires GitHub API access.
- **True lead time to production**: there is no deploy step in the CI history —
  `run-timings.csv` stops at "Upload test results". Deployment frequency and
  commit→prod time need Azure DevOps/App Service deploy logs after go-live.
- **MTTR**: no incident records exist yet; INC-2041/2047 evidence packs are
  training material, not incident-system exports. Wire in PagerDuty/Opsgenie
  timestamps at go-live.

## Cadence

- Capture lead time, CFR, review turnaround monthly from GitHub.
- Re-baseline after the persistence + PayEx work lands (the outbox adds a new
  failure mode — queue depth becomes a fourth metric: outbox rows `Pending` > 5 min).
- Review at each on-call handover; flag any metric that moves > 1 standard
  deviation against this baseline.
