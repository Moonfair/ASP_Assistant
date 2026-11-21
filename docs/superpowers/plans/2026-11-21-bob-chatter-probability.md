# Bob chatter probability adjustment

**Goal:** Attempt random preparation chatter every 5–10 seconds and use the user's 0–1 frequency as the probability of playing each attempt.

**Behavior:** Default frequency is 1. Zero disables only random chatter. A skipped roll schedules a fresh 5–10 second wait. Changes affect the next opportunity without postponing it or resetting event cue deduplication. Previous second-based settings do not carry into the new probability preference. Random chatter may play after 5 seconds once the preceding clip finishes; event cooldowns remain unchanged.

**Implementation:** Replace the interval preference and slider with a persisted double frequency. Inject Random into BobVoiceTriggers for controlled probability tests. Keep the existing voice toggle, preparation checks, cue priority and side-panel opacity.

- [x] Write timing, probability, persistence, UI and playback cooldown tests and observe missing APIs fail.
- [x] Implement probability scheduling, settings, UI copy and playback gate.
- [x] Run focused tests, full solution tests and Release build; review the final diff.

Verification: 76 focused tests passed, followed by 477/477 full solution tests (including deterministic 5-second and 10-second random endpoints). Release build passed with zero warnings/errors. Settings preview visually checked. Independent review found no Critical/Important/Minor issues; live audio was not played during review, and existing event cooldown behavior was preserved and covered by tests.
