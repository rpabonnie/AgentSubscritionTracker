# Agent Memory — AgentSubscritionTracker

> **Append-only. Never edit or delete existing entries.**
> Format: `[YYYY-MM-DD HH:MM UTC] [agent_name] [CATEGORY] message`
> Categories: `DECISION` · `CONTEXT` · `ERROR` · `RESOLVED`
> When this file exceeds 200 lines, move entries older than 30 days to `memory_archive.md`.

---

## Active Memory

[2026-06-09 20:12 UTC] [bootstrap] [CONTEXT] Agent harness initialized for AgentSubscritionTracker. Stack: TBD / TBD. Test command: TBD.
[2026-06-10 21:59 UTC] [orchestrator] [CONTEXT] Stack decided with human: C# / WPF on .NET 10 (net10.0-windows), tests via dotnet test (xUnit). Layout: src/AgentSubscriptionTracker.App + tests/AgentSubscriptionTracker.Tests.
[2026-06-10 21:59 UTC] [orchestrator] [DECISION] Claude usage source: reuse Claude Code local OAuth credentials and query the usage endpoint that powers /usage. Human-approved.
[2026-06-10 21:59 UTC] [orchestrator] [DECISION] Copilot usage source: reuse locally stored GitHub Copilot OAuth token and query the internal quota endpoint (quota snapshots). Human-approved.
[2026-06-10 21:59 UTC] [orchestrator] [DECISION] Secret storage: Windows Credential Manager instead of Azure Key Vault (local desktop app, no cloud footprint). Human-approved; recorded as ADR-0001.
[2026-06-10 21:59 UTC] [orchestrator] [CONTEXT] CLAUDE.md and .github/copilot-instructions.md placeholders filled; tests/ and docs/adr/ created. TASK-001 human checkpoint surfaced to human for sign-off.
[2026-06-10 22:04 UTC] [orchestrator] [DECISION] Human requested security hardening of instructions. Added Security Standards + Engineering Best Practices to CLAUDE.md and copilot-instructions.md: memory-only token handling with log redaction, TLS validation never relaxed, allowlisted API hosts only, no elevation/telemetry/listening sockets, pinned NuGet versions with lock files, analyzers + TreatWarningsAsErrors via Directory.Build.props, no-live-network test policy, TimeProvider injection, single-instance + tray lifecycle rules. Deny lists extended accordingly.
[2026-06-10 22:10 UTC] [code-agent] [CONTEXT] TASK-002 scaffold complete: WPF app (src/AgentSubscriptionTracker.App, net10.0-windows) + xUnit tests (tests/AgentSubscriptionTracker.Tests) in AgentSubscriptionTracker.sln; dotnet build 0 warnings/0 errors, dotnet test 1/1 passed.
[2026-06-10 22:10 UTC] [orchestrator] [CONTEXT] Research workflow complete (2 researchers + 2 adversarial verifiers, both endpoints confirmed). Verified docs written: docs/CLAUDE_USAGE_API_RESEARCH.md and docs/COPILOT_QUOTA_API_RESEARCH.md.
[2026-06-10 22:10 UTC] [orchestrator] [CONTEXT] RISK: no Claude Code .credentials.json and no Copilot token store exist on this machine today. Specs must define a token discovery chain plus a graceful 'not connected / sign in via CLI' state per provider.
[2026-06-10 22:22 UTC] [spec-writer] [CONTEXT] TASK-003 done: SPEC-0001 Claude usage service written (docs/specs/SPEC-0001-claude-usage-service.md) + failing test stubs in tests/AgentSubscriptionTracker.Tests/ClaudeUsage with fake-token fixtures under Fixtures/Claude. RISK flagged in spec: refresh hosts platform.claude.com/console.anthropic.com need an allowlist ADR before the code task closes. Tests reference not-yet-existing AgentSubscriptionTracker.App.Services types, so the solution intentionally does not compile.
[2026-06-10 22:23 UTC] [spec-writer] [CONTEXT] TASK-004 done: SPEC-0002 written (docs/specs/SPEC-0002-copilot-quota-service.md) with ICopilotQuotaService/CopilotQuotaSnapshot contracts, verified token chain + copilot_internal/user mapping, 5 provider states, 30s debounce; failing xUnit stubs in tests/AgentSubscriptionTracker.Tests/Copilot/ + fake-token fixtures in Fixtures/Copilot/ — project intentionally does not compile until TASK-007.
[2026-06-10 22:31 UTC] [spec-writer] [CONTEXT] TASK-005 spec complete: docs/specs/SPEC-0003-tray-tooltip-ui.md (raw Shell_NotifyIcon v4 tray + WPF callout, MVVM contracts in AgentSubscriptionTracker.App.ViewModels consuming SPEC-0001/0002 snapshots) plus failing view-model test stubs in tests/AgentSubscriptionTracker.Tests/Tray/ and token-free snapshot fixtures in Fixtures/Tray/. Tests project intentionally does not compile until TASK-008.
[2026-06-10 22:48 UTC] [code-agent] [CONTEXT] TASK-006 done: SPEC-0001 implemented in src/AgentSubscriptionTracker.App/Services (ClaudeUsageService + credentials reader + models) plus compile-only NotImplementedException skeletons for SPEC-0002/0003 contracts so the test project compiles. dotnet build 0 warnings; ClaudeUsage tests 37/37 green; full run: only SPEC-0002/0003 stub tests red as expected. Fixed test infra: UseWPF-dropped System.IO/System.Net.Http usings in tests csproj; CA1515/CA2000 editorconfig suppressions. OPEN: refresh-host allowlist ADR (platform.claude.com/console.anthropic.com) still required before task close.
