# Lineup Visual Editor Implementation Plan

**Goal:** Apply the user's avatar-grid, full-filter, drag-and-drop and unified editor layout to the existing lineup window.
**Spec:** ../specs/2026-11-10-lineup-builder-design.md (头像与拖放编辑修订)
**Architecture:** Reuse OperatorBrowseViewModel and EquipmentBrowseViewModel for independent catalog filters. LineupEditorViewModel owns selection and equipment assignment; WPF catalog view owns drag sources, roster drop handlers dispatch to tested model operations. Existing persistence/share format remains compatible.

- [x] Add failing tests for filter composition, selected member, equipment placement/replace/clear and bounds; implement model changes.
- [x] Build full catalog filter tabs and avatar grids, local icon converter, roster cards and bottom detail editor, wire drag/drop and live bans.
- [x] Verify real WPF controls and routed drops, real catalog screenshots at default/minimum widths; run full suites and independent review.
- [x] Build a separate preview output if the previous app is running, avoiding loss of its unsaved draft.

Defaults: dropping onto a full portrait does not overwrite; targeted slot drops replace. Keep double-click/add and form selection as alternate interactions.

## Verification record
- Shared filter model tests cover all seven operator dimensions, selected exclusion and equipment classification preservation.
- WPF routed drop tests exercise operator addition, equipment portrait drops, full-slot rejection, specified-slot replacement, wrong payload rejection, clearing and bottom editor selection. Real catalog icons are loaded in both tabs.
- Screenshots at 1020×730 and 800×600 content size verify 4/3 columns, equipment triangle, and visible avatar rows with expanded filters. Fixed null “全部” label rendering and adaptive filter height after failing UI assertions.
- Release test run: `dotnet test ASPAssistant.sln -c Release --no-restore -p:OutputPath=C:/Projects/ASP_Assistant/artifacts/lineup-visual-release/` — 156 passed. Python suite — 8 passed. Separate output keeps the running old application untouched.
- Independent review found no critical/important issues. Verification covers routed WPF drop handling and controls, not OS-level mouse drag initiation. No files were modified by the reviewer.
