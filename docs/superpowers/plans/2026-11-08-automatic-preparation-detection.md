# Automatic preparation detection implementation plan

> Superseded by the user's subsequent explicit core-covenant rule. The current scanner reads the ten top-row core names and their green/gray states, then bans every operator with ANY disabled core covenant. Additional covenants and portrait matching do not participate. See `2026-11-08-core-covenant-bans.md` for current implementation and validation.

**Goal:** Detect the preparation stages automatically without requiring tracked operators or the screenshot button, and accumulate only confirmed banned portraits from the visible ban list.

**Architecture:** A preparation scanner owns phase classification and ban state. The existing timer invokes it before shop tracking. Manual and automatic recognition share a gate. Detect the red ban badges to locate individual portraits, constrain matching to those slots, and reject ambiguous or invalid matches. Show the current phase and scan result in the side panel.

**Scope:** Use existing Maa OCR/SIFT and System.Drawing. Do not control or scroll the game. Overview marks indicate incomplete factions, not a complete ban list. Strategy and custom-squad portraits must not enter the ban matcher. Preserve the previous data refresh.

## Tasks

- [x] Add regression tests for stage recognition, overview/strategy/squad rejection, screenshot badge detection at 1080p and 720p, list accumulation, manual overrides, and clearing in-flight results.
- [x] Implement the preparation scanner, per-card matching, and timer/manual integration. Preserve scanned bans across scrolling and brief OCR misses; provide an explicit new-match reset and automatic reset after leaving preparation for a sustained interval.
- [x] Wire a persistent status message and synchronize the clear button with scanner state. Validate the supplied screenshots, run the suite and build, then restart the locally launched application.

## Review focus

- No tracking selection must not disable automatic preparation detection.
- Overview, strategy portraits, and custom-squad portraits must not produce false bans.
- Scrolled lists may have no title or visible faction label; clipped cards must be deferred.
- Concurrent clear/manual actions must invalidate stale scan results.
- Screenshot tests must use the game client area, excluding the desktop side panel/window title.

## Validation notes

Existing baseline: 18 tests pass; 5 TrackingViewModelTests fail because of an unrelated hard-coded debug-log path. Record this separately unless required by this change.


Completed validation: 24 new regression cases pass; full suite 42 pass / 5 pre-existing TrackingViewModelTests fail on the hard-coded debug-log path. WPF build: 0 warnings/errors. Native Maa replay recognizes all 19 visible banned portraits at both 1080p (~10 s) and 720p (~6 s), and correctly gates all five supplied preparation screenshots. Latest application rebuilt and restarted.

Independent review fixes: missing faction-label fallback now uses visual group markers; first-session initialization no longer invalidates queued captures. Both findings reproduced in failing tests and then passed. A pending-header clear race was also reproduced and fixed. Review left native accuracy, latency and unprovided scroll positions to replay: supplied images/resolutions and simulated clipped cards have been checked; other game UI layouts remain unverified.
