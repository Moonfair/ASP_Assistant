# Batched icons and responsive lineup interactions

User requests: batch icon loading to speed up window opening; also reduce stutter within the panel.

Evidence: CatalogIconConverter synchronously reads/decodes every realized tile on the UI thread, with separate caches per window/view. Standard OperatorCard/EquipmentCard repeat synchronous full-image loading. LineupEditorViewModel replaces AvailableOperators after every member change, regenerating all catalog containers. Search immediately refilters for each keystroke.

Plan (inline, systematic debugging + regression tests):
- [x] Measure real catalog window layout and member-add work; reproduce container replacement.
- [x] Shared thumbnail cache with one background decode worker, batches of eight and a yielding interval; low-priority UI application, unload/path-change guards, missing-image fallback. Use it in lineup and standard browse cards.
- [x] Preserve catalog collection and surviving containers when membership changes; coalesce text entry with a short binding delay; skip unchanged browse results.
- [x] Test batching/deduplication, background decode/failure recovery, rapid image changes, member/container stability and existing interactions. Compare identical local timing probes, run all tests, independent review.

Scope: leave concurrent filter-sidebar work intact. Build into a new artifacts directory without closing the user's running app.

Measured additional cause: 141 tile controls were built up-front even after moving image decode off-thread. Added a fixed-size virtualized tile panel (76×90), realizing the viewport and one extra row on either side. Tests cover surviving containers, scrolling to the last entry, filtering while scrolled, empty results, restored results, 3/4 columns, and existing routed drag/drop behavior.

Local isolated Release timing probe (UI construction + first layout, not all-image completion): before first/warm opens 1780/424/440 ms; after 1041/154/126 ms. Add-one before 378/297/410 ms, after 52/33/32 ms. Reports in ignored artifacts/lineup-performance/. These numbers include runtime warmup and are local measurements, not fixed timing guarantees.

Image tests verify a yield after 8 unique requests, duplicate request/result reuse, off-thread decoding, malformed/missing image recovery, stale/path-cleared/unloaded result rejection, and reload from shared cache. Corrupt PNG initially retained a decoder file handle; switching to a scoped stream fixed the failing cleanup test.

Final validation: full Release suite 161 passed; Python suite 9 passed. The first full run intersected a concurrent filter-sidebar change and failed its new short-window test; rerunning after that task's fix passed without changes to that feature by this task. Independent read-only review found no critical/important issues. Nonblocking coverage note: a deterministic in-flight image completion after path replacement/unload is not separately injected; current tests exercise queued path replacement, unloaded clearing and reload, with version guards reviewed in code.

Output: artifacts/lineup-performance-build/ASPAssistant.App.exe. Existing running application and drafts were left intact. No commit/push/merge.
