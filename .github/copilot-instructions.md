# Copilot Instructions — AgentSubscritionTracker

> GitHub Copilot context for this repository.
> Applies to all Copilot chat sessions, inline suggestions, and autonomous agent runs.
> Keep synchronized with `CLAUDE.md`.

---

## Project

**AgentSubscritionTracker** — A Windows tray widget that on mouseover shows a rich tooltip callout with an at-a-glance overview of Claude (Anthropic) and GitHub Copilot subscription limits and remaining allowance.
Stack: C# / WPF · Runtime: .NET 10 (`net10.0-windows`) · Tests: `dotnet test`

---

## Always

- Use `src/AgentSubscriptionTracker.App/Services/` for external integrations, `.../Utils/` for shared helpers, `tests/` mirroring source structure
- Match the existing language of each module — do not switch languages without explicit user approval
- Store runtime secrets/tokens in **Windows Credential Manager** (per ADR-0001) — never inline credentials
- Keep tokens read from Claude Code / Copilot local stores **in memory only**; redact `Authorization` and `*token*` fields in all logs and exception output
- Use HTTPS with full certificate validation; one shared `HttpClient` per service, explicit timeout, exponential backoff + jitter, honor `429`/`Retry-After`
- Treat API responses as untrusted: strict `System.Text.Json` typed deserialization, handle missing/malformed fields gracefully
- Pin exact NuGet versions, commit lock files (`RestorePackagesWithLockFile`), prefer BCL over new dependencies
- Enforce `<Nullable>enable</Nullable>` + `TreatWarningsAsErrors` + .NET analyzers (`AnalysisMode=All`) via `Directory.Build.props`
- Async/await end-to-end; UI mutations only via WPF `Dispatcher`; inject `TimeProvider` instead of `DateTime.Now`
- Commit `.example` config files alongside any real config that holds secrets
- Write failing tests before writing implementation (TDD); unit tests use fake `HttpMessageHandler` + JSON fixtures, never live network
- Check `agentTask.json` for current work scope before starting new work
- Append all decisions and actions to `memory.md` using: `[YYYY-MM-DD HH:MM UTC] [copilot] [CATEGORY] message`

---

## Never

- Write secrets, API keys, or connection strings in any committed file
- Log, print, or persist token values — including in test fixtures (use fake tokens like `fake-token-for-tests`)
- Disable or relax TLS certificate validation, even "temporarily" or in tests
- Add telemetry, analytics, or calls to hosts other than `api.anthropic.com` / `api.github.com` without an ADR
- Request elevation/admin rights or open listening sockets — this app is outbound-only, standard user
- Skip `tests/` — it is always required, even in personal projects
- Change the language of an existing module without user approval
- Run destructive commands (`rm -rf`, `del /f`, `rmdir /s`) without user confirmation
- Read `.env`, `local.settings.json`, or files matching `*.secret*` or `*.key`
- Mark work complete before `dotnet test` passes

---

## Agent Mode Behavior

When running autonomously:

1. Read `agentTask.json` — understand all tasks, their types, statuses, and `depends_on` chains before doing anything
2. Do not start a task until all its `depends_on` entries are `done`
3. **Stop immediately at any task with `"type": "human_checkpoint"`** — surface it to the user and wait for manual approval
4. Log every significant action to `memory.md` before and after each task
5. On failure: increment `retry_count`, record `last_error` in the task, retry up to `max_retries`
6. At `max_retries`: stop, append to `memory_errors.md`, surface to the user — do not loop

---

## Code Conventions

**C#**: nullable reference types enabled · async/await throughout · no static mutable state
**TypeScript**: strict mode · no `any` · prefer functional style in utilities
**Python**: type hints on all functions · no bare `except` · all I/O through the `services/` layer

---

## Folder Ownership

| Folder | Owner |
|---|---|
| `src/` | Code agent / Copilot |
| `tests/` | Spec writer (stubs) → Code agent (green pass) |
| `docs/specs/` | Spec writer only |
| `docs/adr/` | Human-confirmed decisions only |
| `agentTask.json` | Orchestrator / Copilot agent (status updates only) |
| `memory.md` | All agents — append-only |
