# Removable Filter Tags Implementation Plan

**Goal:** Clicking an active operator filter tag removes only that selection, preserving the operator near the viewport center where possible.

**Architecture:** Give summary tags a dimension and value, route removal through the existing filter ListBoxes, and anchor the virtualized result list to a visible operator across the refresh. Keep tag appearance and existing multiselect semantics.

**Tech Stack:** C#/.NET 8, WPF, xUnit.

**Spec:** User request and screenshot in this chat. If the center operator is excluded by removing one of several OR selections, retain the nearest surviving previous result. Clamp at list boundaries. Empty results remain safe.

**Workspace:** Work in place because the feature extends uncommitted multiselect work. Preserve unrelated changes and the running Debug instance. Build/test Release. No commit or application restart requested.

- [x] Add a failing WPF interaction check for summary-tag clicks, other selections, and automatic “全部” restoration.
- [x] Add typed summary tags and clickable button templates, routing removal by dimension/value.
- [x] Add a failing scroll-position check with virtualized, variable-height cards, then implement capture/restore around removal; suppress scroll-driven filter collapse during restoration.
- [x] Check all seven filter dimensions, duplicate display labels, vanished anchors, wrapped headers, and empty results.
- [x] Run the full test suite and Release build; review only this task's diff against the saved starting files.

Validation: `dotnet test ASPAssistant.sln -c Release --no-restore --verbosity minimal` passed 119/119 tests. `dotnet build ASPAssistant.sln -c Release --no-restore --verbosity minimal` passed with zero warnings/errors. `git diff --check` passed. The scroll regression failed before implementation (centered operator no longer realized); it now verifies screen Y within 2 pixels after dispatcher/layout work settles. Header wrapping, partial panel collapse, removed anchors, rapid clicks, and wheel cancellation are also exercised.

Implementation note: WPF's background virtualization cache measurement changed the initial restored position by about 9 pixels. A single cancellable ContextIdle correction handles that delayed measurement without fighting later user input.

Independent scoped review found no established defect but suggested checking the next user scroll after partial-collapse restoration. That regression failed (10 pixels of scrolling collapsed the panel by about 231 pixels). Rebased the collapse origin from the final offset and preserved panel height, including background refinements and cancellation. The added regression and the final full suite pass (119/119); final Release build has zero warnings/errors. No outstanding review findings.
