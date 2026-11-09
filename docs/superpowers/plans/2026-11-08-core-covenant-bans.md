# Core covenant ban recognition

## Current rule — user correction

The earlier core-only OR rule below is superseded. If an operator has additional covenants, at least one core AND at least one additional covenant must be disabled. If it has no additional covenants, it must have exactly one core and that core must be disabled. All other operators remain available. The user explicitly confirmed the exact-single-core/no-additional interpretation.

The scanner now reads both sections and includes both disabled sets in the observation fingerprint. The complete season covenant catalog is exported as `data/covenants.json`, so rosterless covenants such as 绝技 are included without a hard-coded name list. All additional icon slots must be classified; gray slots must have unique readable names, while a green slot may have an occluded name. Incomplete observations preserve existing bans.

Native replay passed all eight prior cases: latest overview 25 bans at 720p/1080p, original overview 22, windowed overview 24, and no bans from strategy/squad/portrait-list pages. The rule truth table, additional-only changes and clear-during-additional-OCR are covered by regression tests. Logs: `artifacts/covenant-ban-audit/`.

Final validation for this correction: .NET suite 71 passed / 5 pre-existing TrackingViewModel hard-coded-log-path failures; Python suite 6 passed. App build had zero warnings/errors. Read-only review found no actionable issue in the updated rule, state transitions, or catalog wiring.

## Superseded core-only implementation

User-defined behavior: read the complete core covenant row on the information preparation page. Green means available; gray icons with the roster marker mean disabled. An operator is banned if any entry in its CoreCovenants belongs to the disabled set. AdditionalCovenants never contributes. This supersedes the earlier portrait-list interpretation.

- [x] Reproduce the missing top-row bans with the supplied screenshot and native OCR output.
- [x] Recognize names in arbitrary UI order, including merged OCR text boxes, then classify the associated icons at scaled client coordinates.
- [x] Require all ten names and confident icon states before replacing automatic bans. Keep and retry incomplete observations.
- [x] Include all operators directly from the data store, independent of skin templates; remove the SIFT matcher pool from the scan service.
- [x] Preserve stage gating, manual overrides, clear semantics and stale-generation rejection.
- [x] Cover OR matching, multiple cores, additional-only matches, changing rows, occlusion, incomplete OCR, 720p/1080p scaling, and asynchronous clearing with regression tests.
- [x] Replay native OCR on the supplied screenshots, review, build and restart.

The supplied latest screenshot has disabled cores 萨尔贡、谢拉格、阿戈尔、乌萨斯、伊比利亚. The refreshed 141-operator data yields 50 banned operators, including 卡涅利安、哈洛德、锏 through their multiple core covenants.

Validation: 32 related cases passed. Full suite: 50 passed, 5 pre-existing TrackingViewModelTests fail because of the unrelated hard-coded `c:\PlayGround\ASPA\ASP_Assistant\debug-0aa719.log` path. WPF build: zero warnings/errors.

Native Maa OCR replay: all eight cases passed, including the latest screenshot at 720p and 1080p (50 bans), the original overview at both sizes (47 bans), the earlier windowed overview (46 bans), and strategy/squad/portrait-list rejection. Latest replay took ~1.4 s including first recognition warm-up, then ~0.4–0.6 s per overview. Logs and the throwaway audit runner are in ignored `artifacts/core-ban-audit/`.

Review caught stale success status after a transient OCR miss; two failing regression cases reproduced it before the fix. Native replay caught a cursor-obscured heading in the windowed screenshot; a failing fixture test preceded accepting “核心盟”, while retaining the full ten-name and icon checks. The reviewer confirmed both fixes without further actionable findings. Other unprovided layouts remain unverified.
