# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0-alpha] - 2026-06-12

### Added
- First public alpha: Windows tray icon with a rich hover callout showing Claude (5-hour / weekly / per-model limits, extra-usage credits) and GitHub Copilot (premium requests, chat, completions) usage at a glance.
- Inno Setup installer script and local release-packaging script under `installer/` (per-user install, no admin rights required).
- Root `README.md` with an overview of how the app works and install instructions.
- Project version stamped in the app project file (`0.1.0-alpha`).
- Initialized changelog tracking (no canonical source version file found; documentation baseline set to 0.1.0).

### Changed
- Removed the tray icon right-click context menu; app actions are now handled from the callout only.
- Removed the shell hover tooltip text (`AgentSubscriptionTracker`) so it no longer overlaps the rich callout.

### Security
- `CopilotToken.ToString()` is now redacted — the raw OAuth token value can never appear in diagnostic or debug output.
- `ClaudeUsageService` now rejects non-allowlisted endpoints at construction: usage calls must target `api.anthropic.com` and token-refresh calls must target `platform.claude.com` / `console.anthropic.com` over HTTPS (mirrors the existing `CopilotQuotaService` guard).
