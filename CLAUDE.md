<!-- agent-harness-version: 1.1.0 -->

# CLAUDE.md — AgentSubscritionTracker

> Claude Code system context. Loaded automatically on every session.
> Agent behavior in this project is governed by this file and `agentTask.json`.
> Do not delete. Keep synchronized with `.github/copilot-instructions.md`.

---

## Project Identity

- **Name**: AgentSubscritionTracker
- **Description**: A Windows tray widget that on mouseover shows a rich tooltip callout with an at-a-glance overview of Claude (Anthropic) and GitHub Copilot subscription limits and remaining allowance for the current period.
- **Language(s)**: C# (WPF UI + business logic)
- **Runtime**: .NET 10 (Windows, `net10.0-windows`)
- **Test command**: `dotnet test`

---

## Canonical Folder Layout

All agents must respect this structure. Do not create new top-level folders without a `human_checkpoint` task confirming it.

```
<project-root>/
├── src/
│   └── AgentSubscriptionTracker.App/   # WPF tray app (.NET 10)
│       ├── Services/                   # One class per external integration (ClaudeUsageService, CopilotQuotaService, ...)
│       └── Utils/                      # Validators, exceptions, helpers, log capture
├── tests/
│   └── AgentSubscriptionTracker.Tests/ # xUnit. Mirrors source structure. Never optional.
├── docs/
│   ├── specs/         # Spec writer output — SPEC-XXXX-subject.md
│   ├── adr/           # Architecture Decision Records — ADR-XXXX-Subject.md
│   ├── qa/            # QA agent output — QA-XXXX-subject.md
│   └── security/      # Security agent output — SEC-XXXX-subject.md
├── .github/
│   └── copilot-instructions.md
├── CLAUDE.md
├── agentTask.json
├── memory.md
└── memory_errors.md
```

---

## Language-per-Concern Rules

| Concern | Language |
|---|---|
| Business logic, .NET services | C# |
| Azure Functions, AI/automation pipelines | Python 3.13 |
| Frontend utilities, tooling scripts | TypeScript (strict) |

**Agents must not change the language of an existing module without a `human_checkpoint` task.**

---

## Secret Hygiene

- **Never** write secrets, API keys, or connection strings inline or in any committed file.
- This is a local desktop app: runtime secrets/tokens live in **Windows Credential Manager** (per ADR-0001). Azure Key Vault + Managed Identity applies only if cloud components are ever added.
- The app may **read** existing tokens from Claude Code and GitHub Copilot local stores at runtime (in memory only) — it must never copy them into repo files or logs.
- Every config file that contains secrets must have an `.example` counterpart committed to the repo.
- Local development uses `local.settings.json` (gitignored) alongside `local.settings.json.example` (committed).

---

## Security Standards

This app reads OAuth tokens belonging to other applications (Claude Code, GitHub Copilot). Treat every token as highly sensitive, personal project or not.

### Token handling
- Tokens live **in memory only** for the duration of a request; never persisted, cached to disk, or logged.
- Redact `Authorization` and any `*token*` field in all diagnostic output, exception messages, and logs.
- Anything the app itself must persist (cached derived tokens, optional PAT fallback) goes in **Windows Credential Manager** under `AgentSubscriptionTracker/*` (ADR-0001) — never a plaintext file.
- Never prompt the user to paste a token into a config file; point them at the Credential Manager flow instead.

### Network
- HTTPS only, TLS 1.2+; **never** disable or relax certificate validation (no `ServerCertificateCustomValidationCallback` returning true).
- Outbound calls restricted to known hosts: `api.anthropic.com`, `api.github.com`, plus `platform.claude.com` / `console.anthropic.com` for the Claude OAuth refresh grant only (ADR-0002). No other endpoints without an ADR.
- One shared `HttpClient` per service; explicit timeout (≤ 10 s); retry with exponential backoff + jitter; honor `429`/`Retry-After`.
- Treat API responses as untrusted input: strict `System.Text.Json` deserialization into typed models, bounded payload size, never render unvalidated strings into the UI beyond plain text.

### Process hygiene
- Runs as standard user — **no elevation**, no admin manifest, no listening sockets.
- No telemetry or analytics of any kind. Logs are local-only (`%LOCALAPPDATA%\AgentSubscriptionTracker\logs`), secret-redacted, size-capped with rotation.
- Fail closed and degrade gracefully: on auth/API failure the tooltip shows an "unavailable" state for that provider; the app never crashes over a fetch failure and never falls back to insecure behavior.

### Supply chain
- Prefer the BCL; every NuGet dependency must earn its place. Pin exact versions; `RestorePackagesWithLockFile=true` with lock files committed; no prerelease packages.
- `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, .NET analyzers at `AnalysisMode=All` with security (CAxxxx) rules enforced — in a committed `Directory.Build.props`, not per-csproj.

---

## Engineering Best Practices

- **Async discipline**: async/await end-to-end; no `.Result`/`.Wait()`/`GetAwaiter().GetResult()` on the UI thread; all UI mutations via the WPF `Dispatcher`.
- **Single instance**: enforce via named mutex; second launch activates the existing instance.
- **Tray lifecycle**: tray icon disposed on exit (no ghost icons); app survives Explorer restarts (re-add icon on `TaskbarCreated` message).
- **Hover refresh budget**: show cached data instantly on mouseover, refresh in the background (≤ 2 s budget), debounce so hovering repeatedly doesn't hammer the APIs (min refresh interval ~30 s); show data age in the callout.
- **Time correctness**: inject `TimeProvider` (no direct `DateTime.Now` in logic); compute reset countdowns in UTC, display in local time.
- **Testing**: unit tests make **no live network calls** — fake `HttpMessageHandler` with golden-file JSON fixtures under `tests/Fixtures/`; cover malformed/missing-field responses, token-expired paths, and reset-time math edge cases.
- **DI**: constructor injection; every external integration behind an interface (`IClaudeUsageService`, `ICopilotQuotaService`) so the UI layer is testable.

---

## Documentation Convention

- `/docs/` holds per-concern `.md` files — not one monolithic README.
- Every external integration gets `docs/SERVICENAME_SETUP.md`.
- After each major implementation phase, update `docs/IMPLEMENTATION_SUMMARY.md`.
- Technical decisions go through the `codebase-research` skill → ADR in `docs/adr/ADR-XXXX-Subject.md`.

---

## Agent Role Contracts

### Orchestrator
- **Reads**: `agentTask.json`, `memory.md`, `memory_errors.md`
- **Writes**: task `status`, `retry_count`, `last_error`, `updated_at` in `agentTask.json`; `DECISION` entries in `memory.md`
- **Does not**: write implementation code, write spec files, call external APIs directly
- **Gate order**: routes each feature through `spec → code → qa → security → review`; will not advance a feature past a `failed` or `blocked` `qa` or `security` gate
- **Escalates to human** when: `retry_count >= max_retries`, a `human_checkpoint` task is reached, agent output fails spec validation, or the security gate reports a high/critical finding

### Spec Writer
- **Reads**: task entry from `agentTask.json`, domain context from `memory.md`
- **Writes**: `docs/specs/SPEC-XXXX-subject.md` + failing test stubs in `tests/`
- **Does not**: write implementation code, touch `src/`
- **Every spec must include**: an acceptance criteria checklist the code agent must satisfy before closing the task

### Code Agent
- **Reads**: `docs/specs/SPEC-XXXX-subject.md`, failing tests in `tests/`
- **Writes**: implementation in `src/` (`Services/`, `Utils/`, or applicable source folder)
- **Does not**: modify spec files, mark a task `done` before `dotnet test` passes green
- **On test failure**: increments `retry_count`, records `last_error`, retries up to `max_retries`

### QA Agent
- **Reads**: implementation in `src/`, `tests/`, the acceptance-criteria checklist in `docs/specs/SPEC-XXXX-subject.md`, the task entry in `agentTask.json`
- **Writes**: a quality report in `docs/qa/QA-XXXX-subject.md`, follow-up improvement tasks in `agentTask.json` (type `qa` or `review`), `DECISION`/`CONTEXT` entries in `memory.md`
- **Does not**: write or refactor implementation code itself, modify spec files, mark a `code` task `done` on the code agent's behalf, or lower acceptance criteria to make a check pass
- **Runs the quality gate**: confirms `dotnet test` passes green; verifies every acceptance-criteria item in the spec is met; assesses readability, duplication, cyclomatic complexity, naming, error handling, and test coverage; lists concrete, prioritized improvements; flags untested branches and missing edge-case tests
- **Fails the gate / escalates to human when**: `dotnet test` is red, an acceptance-criteria item is unmet, coverage drops below the project threshold, or a high-severity quality defect has no owner

### Security Agent
- **Reads**: all source, dependency manifests (`*.csproj`, lock files), config and `.example` files, `docs/specs/`, the task entry in `agentTask.json`
- **Writes**: a security report in `docs/security/SEC-XXXX-subject.md`, remediation tasks in `agentTask.json` (type `security`), `DECISION`/`CONTEXT` entries in `memory.md`
- **Does not**: implement new features, weaken or disable a security control to make a test or build pass, suppress or downgrade a finding without a recorded human decision, or commit a fix that introduces a secret
- **Adversarial posture toward the code agent**: treats the code agent's output as **untrusted by default — insecure until proven secure**, and the burden of proof is on the code agent. Does not accept "it's handled" / "this is safe" without seeing the control *in the code* or a test that actually exercises it. Independently re-reads and re-runs rather than trusting prior agents' summaries.
- **Red-teams every finding**: states a concrete attack/abuse scenario — the exact malicious input, request, or sequence that breaks the control — not a vague warning. Rejects any remediation that merely silences the symptom (e.g. broadens an allowlist, swallows the error, disables the check) instead of fixing the root cause, and re-opens the `security` task if a "fix" does not hold up to the same attack.
- **Runs the security gate** (OWASP-aligned): scans for inline secrets/keys/tokens; checks input validation and output encoding; injection (SQL/command/path/template); authentication and authorization on every sensitive path; insecure deserialization; SSRF; secure defaults and least privilege; PII and secret leakage in logs; transport security (TLS) and CORS; known-vulnerable dependencies; error handling that does not leak internals
- **Has blocking authority**: may set its `security` gate task to `blocked`/`failed` to stop the chain until the issue is remediated — a code task does not pass the gate just because the code agent disagrees with a finding; disputes go to a `human_checkpoint`, not to the code agent's discretion
- **Escalates to human immediately when**: a secret is found in a committed file, a sensitive path lacks authorization, or any high/critical-severity issue is found

---

## agentTask.json Conventions

- Schema defined in the `agent-harness` skill → `templates/agentTask.schema.json`
- Only the orchestrator updates task status
- `depends_on` is enforced — no task starts until all listed dependencies are `done`
- `human_checkpoint` tasks block the entire chain until manually set to `done`
- `type` values: `spec` · `code` · `test` · `qa` · `security` · `review` · `human_checkpoint`
- `assigned_to` values: `orchestrator` · `spec_writer` · `code_agent` · `qa_agent` · `security_agent` · `human`
- `status` values: `pending` · `in_progress` · `done` · `failed` · `blocked`

---

## memory.md Conventions

- **Append-only.** Never edit or delete existing entries.
- Entry format: `[YYYY-MM-DD HH:MM UTC] [agent_name] [CATEGORY] message`
- Categories: `DECISION` · `CONTEXT` · `ERROR` · `RESOLVED`
- When `memory.md` exceeds 200 lines, move entries older than 30 days to `memory_archive.md`

---

## Agent Deny List

Agents must **never**:
- Run `rm -rf`, `del /f`, `rmdir /s`, or any irreversible filesystem operation without a `human_checkpoint` task
- Read `.env`, `local.settings.json`, or any file matching `*.secret*` or `*.key`
- Write credentials, tokens, or connection strings to any file
- Mark a `code` task `done` without a passing `dotnet test` run
- Modify another agent's scope (code agent must not edit spec files; spec writer must not edit source)
- Create new top-level folders without a `human_checkpoint` task confirming it
- Disable or relax TLS certificate validation, even "temporarily" or in tests
- Log, print, or persist token values — including in test fixtures (fixtures use fake tokens like `fake-token-for-tests`)
- Add telemetry, analytics, or network calls to hosts other than `api.anthropic.com` / `api.github.com` without an ADR
- Add a NuGet dependency without pinning its exact version
- Weaken, disable, or bypass a security control to make a build or test pass
- Suppress, delete, or downgrade a QA or Security finding without a recorded human decision in `memory.md`
- Mark a `qa` or `security` gate task `done` without actually running its checks
