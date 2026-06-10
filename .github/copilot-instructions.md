# Copilot Instructions — AgentSubscritionTracker

> GitHub Copilot context for this repository.
> Applies to all Copilot chat sessions, inline suggestions, and autonomous agent runs.
> Keep synchronized with `CLAUDE.md`.

---

## Project

**AgentSubscritionTracker** — A Windows widget that on mouseover gives a quick glance into Copilot and Anthropic subscriptions.
Stack: TBD · Runtime: TBD · Tests: `TBD`

---

## Always

- Use `services/` for external integrations, `utils/` for shared helpers, `tests/` mirroring source structure
- Match the existing language of each module — do not switch languages without explicit user approval
- Use **Azure Key Vault + Managed Identity** for all secrets — never inline credentials
- Commit `.example` config files alongside any real config that holds secrets
- Write failing tests before writing implementation (TDD)
- Check `agentTask.json` for current work scope before starting new work
- Append all decisions and actions to `memory.md` using: `[YYYY-MM-DD HH:MM UTC] [copilot] [CATEGORY] message`

---

## Never

- Write secrets, API keys, or connection strings in any committed file
- Skip `tests/` — it is always required, even in personal projects
- Change the language of an existing module without user approval
- Run destructive commands (`rm -rf`, `del /f`, `rmdir /s`) without user confirmation
- Read `.env`, `local.settings.json`, or files matching `*.secret*` or `*.key`
- Mark work complete before `TBD` passes

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
| `services/` | Code agent / Copilot |
| `utils/` | Code agent / Copilot |
| `tests/` | Spec writer (stubs) → Code agent (green pass) |
| `docs/specs/` | Spec writer only |
| `docs/adr/` | Human-confirmed decisions only |
| `agentTask.json` | Orchestrator / Copilot agent (status updates only) |
| `memory.md` | All agents — append-only |
