# CLAUDE.md — AgentSubscritionTracker

> Claude Code system context. Loaded automatically on every session.
> Agent behavior in this project is governed by this file and `agentTask.json`.
> Do not delete. Keep synchronized with `.github/copilot-instructions.md`.

---

## Project Identity

- **Name**: AgentSubscritionTracker
- **Description**: A Windows widget that on mouseover gives a quick glance into Copilot and Anthropic subscriptions.
- **Language(s)**: TBD
- **Runtime**: TBD
- **Test command**: `TBD`

---

## Canonical Folder Layout

All agents must respect this structure. Do not create new top-level folders without a `human_checkpoint` task confirming it.

```
<project-root>/
├── services/          # One file per external integration (e.g. gemini_service.py, notion_service.py)
├── utils/             # Validators, exceptions, helpers, log capture
├── tests/             # Mirrors source structure. Never optional.
├── docs/
│   ├── specs/         # Spec writer output — SPEC-XXXX-subject.md
│   └── adr/           # Architecture Decision Records — ADR-XXXX-Subject.md
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
- All secrets go in **Azure Key Vault**, accessed via Managed Identity only.
- Every config file that contains secrets must have an `.example` counterpart committed to the repo.
- Local development uses `local.settings.json` (gitignored) alongside `local.settings.json.example` (committed).

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
- **Escalates to human** when: `retry_count >= max_retries`, a `human_checkpoint` task is reached, or agent output fails spec validation

### Spec Writer
- **Reads**: task entry from `agentTask.json`, domain context from `memory.md`
- **Writes**: `docs/specs/SPEC-XXXX-subject.md` + failing test stubs in `tests/`
- **Does not**: write implementation code, touch `services/` or `utils/`
- **Every spec must include**: an acceptance criteria checklist the code agent must satisfy before closing the task

### Code Agent
- **Reads**: `docs/specs/SPEC-XXXX-subject.md`, failing tests in `tests/`
- **Writes**: implementation in `services/`, `utils/`, or applicable source folder
- **Does not**: modify spec files, mark a task `done` before `TBD` passes green
- **On test failure**: increments `retry_count`, records `last_error`, retries up to `max_retries`

---

## agentTask.json Conventions

- Schema defined in the `agent-harness` skill → `templates/agentTask.schema.json`
- Only the orchestrator updates task status
- `depends_on` is enforced — no task starts until all listed dependencies are `done`
- `human_checkpoint` tasks block the entire chain until manually set to `done`
- `type` values: `spec` · `code` · `test` · `review` · `human_checkpoint`
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
- Mark a `code` task `done` without a passing `TBD` run
- Modify another agent's scope (code agent must not edit spec files; spec writer must not edit source)
- Create new top-level folders without a `human_checkpoint` task confirming it
