# Adaptive covenant recognition

The user approved implementing the self-calibrating recognition flow discussed in this conversation.

## Requirements

- Locate covenant icons from the image and use their measured dimensions for state sampling and label crops.
- Support translated, scaled and letterboxed preparation layouts with broad OCR fallback when the usual bands miss.
- Read small labels in isolated enlarged crops and validate names, complete slot coverage and icon states before publishing.
- Reuse measured geometry while validating it on every frame. Clear it when the source, size, round or layout changes.
- Preserve existing mode pools, grouped selection, overview gating, manual overrides and generation checks.

## Implementation

- [x] Add real-image regression cases for displaced layouts, low-resolution label rereads and incomplete observations. Watch the displaced-layout case fail.
- [x] Implement image-derived icon rows, measured state sampling and isolated label OCR. Verify the locator against existing preparation and overview fixtures.
- [x] Connect adaptive recognition and bounded geometry reuse to the scanner, including broader phase discovery and invalidation.
- [x] Run the complete .NET suite, replay existing and transformed screenshots, build the app and obtain an independent code review.

## Review focus

Wrong-phase false positives, obscured gray icons, duplicate or missing names, reduced mode pools, delayed OCR after a target/round change, and geometry retained after a window resize.

## Execution record

Continue in the shared checkout containing the preceding screenshot fix so the user's existing changes stay together. The implementation adds no dependency or release action. Screenshot transformations verify geometry handling. Authentic non-16:9 game reflow still requires real samples.

The three displaced-layout cases failed before implementation, then passed. The grouped-layout case failed before measured group detection, then passed. A split-heading case reproduced a discovery exception before its fix. The existing 119 preparation/covenant cases passed before the final safety additions.

Final verification used `dotnet test ASPAssistant.sln -c Release --no-restore`. All 413 cases passed. The app and test projects built successfully. Native replay covered 15 existing preparation, overview and shop screenshots. New regressions cover a 720 by 480 canvas, far-right grouped layouts, switching groups on cached geometry, same-size moves, resize/target reset, and preserving confirmed results after occlusion.

Independent review found a grouped OCR crop that still depended on capture width. Its far-right reproducer failed before the measured-area fix and passed afterward. A second-frame group-switch check also passed. A separately reproduced core-row substitution bug was fixed by constraining rows to their section headings. Extremely small text receives one grayscale reread while retaining strict vocabulary matching. The reviewer reported no remaining critical or important findings. Authentic game UI reflow and sustained live-capture throughput remain follow-up validation.

The reported 极速模拟 screenshot exposed a missing group-selection flag in the mode catalog. Native replay failed with the shipped flag and passed with group selection enabled. The catalog and its generator now retain that flag, with an original-image regression and a generator regression that both failed before the fix. Verification passed 132 preparation/covenant tests and all 15 Python data tests. The rebuilt Release application was launched. Screenshot replay identified the four selected core covenants, two disabled additional covenants, and 71 banned operators. Live observation was already in round 1, so verification of the next preparation screen remains with the next round.
