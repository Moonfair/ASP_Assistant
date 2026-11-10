# Lineup drag icon preview

User request: show the dragged operator/equipment image following the pointer with a small amount of lag.

Use a hit-test-transparent adorner above the editor content. Reuse the shared deferred icon loader, ease the visual position with a 50 ms time constant, and leave native drag/drop targeting unchanged. Track the screen pointer during the native drag loop, converting to WPF coordinates for DPI correctness. Hide outside the editor; remove the preview and rendering subscription on drop, cancellation, exceptions, or editor unload.

- [x] Add focused tests for smooth movement, frame-rate independence, shared images, hit testing, and cleanup.
- [x] Add the preview and connect both catalog drag types with scoped cleanup.
- [x] Verify the rendered preview and existing drop behavior; run the Release suite and review.

Preserve concurrent filter-sidebar work and running app drafts. Build into artifacts/lineup-drag-preview-build.

Validation: Release suite 164/164 passed. Native UI verification used a temporary standalone editor, without touching the running user app or saved lineups. Dragging an operator onto the roster and equipment onto its portrait both produced Copy drops and the expected member/equipment state. Active-drag captures show the correct shared icons above the editor. Fixed placeholder text showing through transparent equipment images and added regression coverage. Scoped disposal and unload remove the preview and rendering subscription; deterministic tests cover lag, settling, frame-rate independence, hiding/re-entry, and stopping after disposal.

Review: no critical/important findings. Limits: automation produces a very brief jump-and-release drag, so continuous OLE-time animation, mixed-DPI dragging, and Escape cancellation were not independently exercised through native input. Native captures and event traces are in ignored artifacts/drag-preview-check/. The temporary test process was closed. No commit/push/merge.
