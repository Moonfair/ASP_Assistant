# Browse Filter Multiselect Implementation Plan

**Goal:** Sort operators by ascending tier and enable multiselect for all operator/equipment tag filters except operator availability and stacking.

**Architecture:** Keep the existing WPF chip lists. Store selected values as sets in each view model, using OR within a filter and AND across filters. Share selection normalization between both views so the first “全部” item is exclusive and restored when the last specific tag is removed.

**Tech Stack:** C#/.NET 8, WPF, xUnit.

**Requirements:** User requests in this chat, including confirmation that equipment tier and type are included. Keep the current equipment type choices. Keep equal-tier operator source order. Preserve existing work in the checkout.

- [x] Verify the existing unsorted operator result with a failing test.
- [x] Add regression cases in OperatorBrowseViewModelTests and EquipmentBrowseViewModelTests covering multiple values, cross-filter intersections, clearing, search, and ordinary/elite traits.
- [x] Replace scalar tier/trait/timing/type filters with SetTierFilters, SetTraitTypeFilters, SetTriggerTimingFilters, and SetTypeFilters. Sort operator results at the shared filtering exit point.
- [x] Add shared FilterChipSelection behavior to the five operator lists and two equipment lists. Wire SelectedItems into view models and keep the two exception lists single-select.
- [x] Verify real WPF selection behavior (mouse toggles, “全部”, last-item deselection, multiple active tags) and view bindings.
- [x] Run the complete .NET test suite and build the app in Release, leaving the currently running Debug instance intact.

Review focus: null/empty “全部” items, selection before containers exist, multiple tags across ordinary/elite variants, filters yielding zero results, and preserving ascending order after clearing or ban changes.

Validation: Release build succeeded; 109/109 .NET tests passed. Real Windows mouse checks confirmed operator tiers 1+3 and equipment types 普通+转职 remain selected together. Independent scoped review found no actionable issues. The temporary Release instance was closed; the original Debug instance remains running.
