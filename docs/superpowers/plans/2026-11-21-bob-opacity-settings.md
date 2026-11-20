# Bob and opacity settings implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Add persistent random Bob speech frequency and whole-side-panel opacity controls to settings on main.

**Architecture:** Extend SettingsManager with validated numeric preferences; make BobVoiceTriggers' existing random delay configurable. Add settings controls through a SidePanelWindow partial and wire them from App. Use WPF's supported transparent windows and Window.Opacity while retaining WindowChrome.

**Tech Stack:** .NET 8, WPF, Win32, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-21-bob-opacity-settings-design.md`

## Global Constraints

- Chatter interval 10–120 seconds, default 15, actual random wait between interval and twice interval.
- Side-panel opacity 20–100%, default 100%.
- Keep unrelated work in the original checkout intact; work on main in the separate checkout.
- No new dependencies; preserve existing Bob event cues, WindowChrome and resize grip.

## Review Focus

- Missing/corrupt/out-of-range saved preferences restore defaults.
- Changing chatter frequency replaces the pending wait without resetting combat deduplication.
- Resetting a match preserves the configured interval.
- Opacity loaded before window creation survives HWND creation and resizing; later changes apply immediately.
- Disabled/unavailable voice disables chatter controls without discarding preferences.

### Task 1: Persist preferences and configure chatter

**Files:** SettingsManager.cs, BobVoiceTriggers.cs, SettingsManagerTests.cs, BobVoiceRuleTests.cs.

**Interfaces:** `Load/SaveBobChatterIntervalSeconds`, `Load/SaveSidePanelOpacity`, `BobVoiceTriggers.ChatterIntervalSeconds`.

- [x] Add and run failing persistence/validation and chatter timing tests.
- [x] Add bounded numeric settings with existing atomic persistence.
- [x] Use configurable random delays, clearing only the pending chatter timer when the interval changes.
- [x] Verify focused tests.

### Task 2: Settings UI and runtime wiring

**Files:** SidePanelWindow.xaml, SidePanelWindow.Preferences.cs, App.xaml.cs, BrowseFilterInteractionTests.cs, README.md.

**Interfaces:** `ConfigureBobChatter(int, Action<int>)`, `ConfigureSidePanelOpacity(double, Action<double>)`.

- [x] Add interaction checks to the existing resource-initialized WPF test for live preference changes, native transparency support and opacity before/after HWND creation and resizing.
- [x] Add labeled sliders and initialization/callbacks; disable chatter slider with the existing voice toggle.
- [x] Wire restored preferences to live behavior and saves; document the controls.
- [x] Run focused tests, whole .NET suite and Release build; review diff before committing on main.

## Verification record

- Focused settings, Bob voice and WPF interaction tests: 57 passed, 0 failed.
- Complete .NET solution test suite: 457 passed, 0 failed (4.10 minutes).
- Release solution build: 0 warnings, 0 errors.
- Desktop smoke: 50% opacity changed an opaque RGB (26,26,46) pixel over a lime backdrop to (13,141,23), confirming real alpha compositing. A native right-edge hit test returned HTRIGHT (11), confirming resizing remains active.
- Independent code review: no Critical, Important, Minor or declined-to-judge findings.
