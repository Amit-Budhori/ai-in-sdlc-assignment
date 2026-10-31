# Risk Audit — Agent Setup vs. the Lethal Trifecta

The lethal trifecta (Simon Willison, 2025) says an agentic system becomes dangerous
when all three legs are present at once:

1. **Access to private/sensitive data** — the agent can read things you do not want to leak
2. **Exposure to untrusted content** — the agent ingests text an attacker controls
3. **Ability to act externally** — the agent can send data or trigger effects outside your trust boundary

Any two are manageable. All three mean an attacker who plants instructions in the
untrusted content can direct the agent to read the private data and exfiltrate it
through the external action channel.

## The setup as described

The agent setup in this repository:

- AI coding agent runs in this repo with `AGENTS.md` loaded as standing instructions.
- `.mcp.json` registers a Playwright MCP server: `npx @playwright/mcp@latest` —
  a browser-automation tool that fetches and renders arbitrary web pages, and is
  resolved to the newest published version on every session start.
- The `ops/` evidence packs (incident logs, metrics, deploy diffs, Service Bus
  inspection output) are in-repo and readable.
- The agent can run shell commands (`dotnet`, `npx`, `git`) and edit files —
  the e2e workflow `cd e2e && npx playwright install chromium && npm test` is
  documented in `AGENTS.md`.

## Leg-by-leg audit

| Leg | Present? | Evidence |
|-----|----------|----------|
| Access to private data | **Yes** | Full source, `ops/incidents/*` evidence packs with internal logs, metrics, and config details; real deploy SHAs and engineer names in `recent-changes.md`; in production this grows to real customer invoices and PayEx credentials via environment/config. |
| Exposure to untrusted content | **Yes** | Two vectors: (a) `@playwright/mcp@latest` browses arbitrary pages whose content feeds back into the agent context — web pages are a classic prompt-injection carrier; (b) `npx @playwright/mcp@latest` executes *code* resolved from npm at session start — the `@latest` tag means the executed code is whatever was published most recently, i.e. attacker-influenceable supply-chain content running with the agent's privileges. |
| Ability to act externally | **Yes** | Playwright MCP can navigate to and submit data on external sites; shell access means `curl`/network egress is one hallucinated command away; `git push`/`gh` could publish if credentials exist in the environment. |

**All three legs are present.** The browser MCP is the critical joint: it is both the
untrusted-content intake *and* a read/write channel to the outside world.

## Decision

**Redesign — remove the untrusted-content leg.** Playwright MCP is not needed for
anything in this assignment or in day-to-day development on this service (the e2e
suite runs via `npx playwright test` in CI, not via an interactive MCP session).

Concretely:

1. Remove the `playwright` server from `.mcp.json`. If a browser tool is genuinely
   needed later, pin it (`@playwright/mcp@<exact-version>`), scope it to
   `localhost:5000` only, and never leave `@latest` in a config the agent loads
   automatically — an unpinned remote package executed at session start is
   arbitrary-code-execution-by-supply-chain even without prompt injection.
2. Keep legs 1 and 3 with controls rather than elimination: the agent needs repo
   access to be useful, and it needs to run `dotnet test`. What it loses is a
   live internet channel while it holds that access.

After the change, the trifecta no longer holds: an attacker cannot get instructions
into the agent's context through a channel the agent also uses to exfiltrate.

## What could still go wrong (residual risk)

- **Ops evidence is not fully private**, but it is sensitive-adjacent (internal
  topology, engineer names, deploy SHAs). If this agent's transcripts sync to a
  third-party service, the evidence packs should be scrubbed or kept out of scope.
- **Prompt injection via code itself.** A malicious PR could embed instructions
  in comments/README that the agent reads. This is mitigated by review, not by the
  trifecta redesign — worth noting because it bypasses the untrusted-content
  control entirely.
- **Dependency-install egress.** `dotnet restore`/`npm ci` still hit package feeds;
  a typosquat in a suggested package could reach the dev box. CI's dependency and
  licence scans partially cover this.

## Revisit triggers

- The team adds any web-browsing, email-reading, or chat-ingestion tool back into
  the agent's MCP set → re-audit; the trifecta likely reforms.
- The agent gains credentials that can reach production (Service Bus connection
  strings, PayEx keys in local config) → the private-data leg becomes materially
  worse; require a secrets vault and read-only scopes before re-enabling tools.
- First confirmed prompt-injection attempt observed in logs or reported upstream
  → revisit whether code-comment injection needs active scanning.
- `@latest` or any floating version reappears in an MCP/tooling config → treat as
  a policy violation; the revisit trigger is a CI lint that fails on `@latest`.
