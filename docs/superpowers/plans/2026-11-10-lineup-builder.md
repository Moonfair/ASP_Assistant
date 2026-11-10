# Lineup Builder Implementation Plan

> Execute inline using superpowers:executing-plans; review the completed change with a fresh reviewer.

**Goal:** Add persistent, shareable lineups with up to ten operators, core flags, two equipment slots and author attribution.
**Architecture:** Core owns records, versioned text validation, atomic persistence and editor/library view models. WPF owns windows, selection UI and clipboard access.
**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, System.Text.Json, xUnit.
**Spec:** docs/superpowers/specs/2026-11-10-lineup-builder-design.md

## Constraints and review focus
- Preserve existing uncommitted work; branch codex/lineup-builder in the current checkout.
- Global creation remains usable while OCR overlay is visible; editor is nonmodal.
- Invalid text, unknown names, future versions and null collections must not modify stored lineups.
- Editing/importing must not mutate the source before save; import always gets a new identity.
- Failed disk/clipboard access must keep user work available and give actionable feedback.
- Reopened lineups keep authors, core flags and both equipment slots, including repeated equipment.

## Tasks
- [x] Core models, LineupShareService, LineupStore: failing roundtrip, validation and disk tests, then implementation.
- [x] LineupEditorViewModel and LineupLibraryViewModel: failing selection limit, snapshot isolation, persistence tests, then implementation.
- [x] LineupView, LineupEditorWindow, global button, startup wiring: bind real controls and verify interactions/layout; document usage.
- [x] Full Release tests and Python suite, fresh review, fix substantive findings, final diff audit.

## Execution record
- Baseline: 135 .NET tests passed before changes.
- Work in current checkout on requested new branch; existing dirty files retained. No automatic commits of unrelated changes.
- WPF tests exercise actual name/author fields, add button, core checkboxes, both editable equipment controls, save, valid/invalid import preview and 278px list layout. Offscreen screenshots inspected and dark-theme contrast corrected.
- Independent reviewer found owner shutdown could bypass unsaved-draft prompts. Main-window Closing now preflights every owned lineup editor, including new drafts; shutdown runs only after a successful close. The shared close preflight is exercised for accept/reject without mutating draft state.
- Read errors now distinguish missing files from inaccessible files/directories. A failing directory-at-file-path regression test was added before the fix.
- Final verification: `dotnet test ASPAssistant.sln -c Release --no-restore` passed 152/152; `python -B -m unittest discover -s tests` passed 8/8; `git diff --check` passed. Offscreen editor/library PNGs are in ignored `artifacts/lineup-preview/`.
- Work remains uncommitted on `codex/lineup-builder` for user review; no merge, push or release performed.
