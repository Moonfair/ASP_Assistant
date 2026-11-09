# Release hardening implementation plan

> **For agentic workers:** Use superpowers:executing-plans for inline implementation. Preserve the existing uncommitted application and data changes.

**Goal:** Resolve the release audit's updater path/cache, settings recovery, native dependencies, packaging size and local-path residue issues.

**Architecture:** Keep per-user settings under AppData. Use atomic settings replacement and preserve malformed originals. Separate the updater's PowerShell program from JSON parameters; use a unique disposable update job directory. Publish only Windows x64 runtime and active resources, adding Visual Studio redistributable CRT files beside the Maa native DLLs.

**Tech Stack:** .NET 8, WPF, xUnit, PowerShell 5.1 updater, PowerShell 7 release script.

**Spec:** User-approved findings from this chat and `artifacts/release-audit/audit-results.json`.

## Global Constraints

- Windows 10/11 x64; keep existing runtime behavior and all current working-tree data.
- No source commits, tagging, uploads or installation into the host OS as part of this repair.
- Output is a validation build with version 0.0.0, not a chosen public release version.
- Never delete a shared update directory belonging to another process; generated jobs are isolated.

## Review Focus

- Unicode, spaces, quotes, dollar signs and brackets must remain literal paths.
- Failed downloads, failed copies and successful updates must clean only their own temporary job.
- Invalid/null settings and competing saves must not break startup or destroy the last valid file.
- Missing redistributables and reused publish output must stop packaging, not produce an incomplete ZIP.
- Reduced resources and CRT dependencies must be verified by native OCR from the published folder.

### Task 1: Settings persistence
- [x] Add invalid JSON/null entry recovery and preservation tests; run them red.
- [x] Serialize access, write a temporary sibling and atomically replace tracking.json; back up invalid files and return defaults.
- [x] Verify settings tests.

### Task 2: Update installation
- [x] Preserve the audit's failing PowerShell path reproductions as baseline; add integration coverage executing the production installer against disposable fixtures.
- [x] Embed a parameterized PowerShell installer and JSON manifest written with UTF-8 BOM. Use LiteralPath, explicit error handling, bounded process wait and per-job cleanup.
- [x] Use GUID update jobs; clean download/extraction/launch failures; validate the package and target writability before shutdown.
- [x] Verify Unicode/metacharacter paths, success/failure cleanup and invalid package behavior.

### Task 3: Release packaging
- [x] Use only Maa binding and win-x64 runtime packages; explicitly include active catalogues and icons; exclude legacy matching resources and publish PDBs.
- [x] Add one local/CI release script that refuses nonempty output, locates x64 Visual Studio CRT redistributables and verifies required payload files.
- [x] Update CI to test and invoke that script; document the publish command and runtime/cache locations.
- [x] Ignore Python caches and remove the old debug-path write.

### Task 4: Verification
- [x] Run the complete .NET and Python suites.
- [x] Build a fresh self-contained package; check resources, dependencies, unwanted files and size.
- [x] Run published native OCR from a Unicode path and verify CRT modules resolve from the package.
- [x] Obtain a fresh review and resolve material findings; record results below.

## Execution record

Ruling: Work in the existing checkout without staging or committing — the fixes must apply to the user's already audited uncommitted features/data; a new checkout would omit them. Cost: the final working tree contains both the user's prior work and these changes.

Ruling: Use native PowerShell and the existing ignored artifacts directory for verification records instead of shell-specific skill helper scripts. The plan checkboxes and test output are the completion ledger.

Baseline: audit build succeeds; 119 .NET and 8 Python tests pass. Empty tracking JSON throws JsonException. Production Copy-Item expressions fail for Chinese and dollar paths and silently copy nothing for bracket paths.


Completed verification: 134/134 .NET tests and 8/8 Python tests pass; git diff --check passes. Settings red run failed 7 regressions before implementation. Updater special-path test caught Start-Process provider interpretation; direct ProcessStartInfo resolves it. Package verification caught referenced Core.pdb; Directory.Build.targets now excludes all publish PDBs.

Native evidence: moving CRT files to the executable folder was insufficient (all five CRT modules still loaded from System32). Placing them beside Maa DLLs made every CRT module load from the package; OCR returns 53 results in a Chinese/space directory. Final publish is 785 files / 283.75 MiB; ZIP integrity passes (131.39 MiB). See artifacts/release-hardening/verification.json.

Final review: fresh read-only reviewer found no material correctness/regression issues. Source review cannot replace clean-machine full UI testing; a published native probe supplies partial evidence.

Final ruling: preserve obsolete files already present in an installed application during overlay updates — deleting unknown existing content is outside this package-cleanup repair. Cost: upgrades over an old installation will not reclaim its obsolete asset space; a fresh extraction gets the reduced package.

Final ruling: do not add cross-process settings locking or hardware power-loss durability beyond atomic replacement — the demonstrated regression is concurrent saves in one instance. Cost: multiple independently running app instances may overwrite each other's latest state.
