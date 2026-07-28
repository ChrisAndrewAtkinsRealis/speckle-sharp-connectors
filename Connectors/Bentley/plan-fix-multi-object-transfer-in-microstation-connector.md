# 🎯 Fix multi-object transfer in MicroStation connector

## Understanding
The user reports that single objects transfer correctly, but multiple selected objects do not. The screenshots suggest that when multiple items are sent, the resulting Speckle model collapses into tiny or sparse geometry, so the issue is likely in the send aggregation/batching path rather than in individual element conversion.

## Assumptions
- The problem is in the MicroStation send pipeline, not the receive pipeline.
- Single-element conversion works, so the element-level converters are mostly correct.
- The bug is likely in selection handling, root object assembly, instance wrapping, or model/document caching.
- The active file `MicroStationDocumentModelStore.cs` may be related to how multiple objects are tracked or grouped.

## Approach
First, inspect the send pipeline around selection collection, root object building, and any model store or grouping logic that distinguishes single vs multiple selections. Then compare the single-object and multi-object code paths to identify where display values, transforms, or child objects may be dropped or double-wrapped.

Next, patch the smallest shared component that is responsible for aggregating multiple selected elements into the Speckle root so that it preserves all objects and their display values. Validate by building the solution and, if necessary, adjust any host-specific model/document store assumptions that break multi-selection.

## Key Files
- `Connectors/Bentley/Speckle.Connectors.MicroStationShared/HostApp/MicroStationDocumentModelStore.cs` - likely tracks the active model/document state used during sends.
- `Connectors/Bentley/Speckle.Connectors.MicroStationShared/Operations/Send/MicroStationRootObjectBuilder.cs` - likely assembles the final root object from selections.
- `Connectors/Bentley/Speckle.Connectors.MicroStationShared/Operations/Send/MicroStationRootObject.cs` - root payload structure for sent objects.
- `Connectors/Bentley/Speckle.Connectors.MicroStationShared/Bindings/MicroStationSendBinding.cs` - entry point for send workflow.
- `Connectors/Bentley/Speckle.Connectors.MicroStationShared/HostApp/MicroStationInstanceBaker.cs` and `MicroStationInstanceUnpacker.cs` - may affect multi-object grouping or transforms.

## Findings

`MicroStationDocumentModelStore.cs` was a red herring: it only persists DUI3
model-card JSON into the DGN file's EC data (stream-state equivalent) and has
no involvement in send aggregation. `MicroStationRootObjectBuilder`,
`MicroStationSendBinding`, `MicroStationSelectionBinding` and
`InstanceObjectsManager` were all traced end-to-end and are correct: each send
gets a fresh DI scope (`SendOperationManagerFactory.Create()`), so the
per-builder level/reference collection caches don't leak across sends, and
selection/application-id handling is per-element with no shared mutable state.

The real bug is in `MicroStationInstanceUnpacker.UnpackSharedCell` (send,
shared-cell instancing). When the true `SharedCellDefinitionElement` can't be
resolved via `FindSharedCellDefinition`, the code fell back to
`EnumerateChildren(instance)` — the **placement's own children in
world/placed coordinates** — and stored that as the shared
`InstanceDefinitionProxy`. Every placement of the same shared cell reuses one
`InstanceDefinitionProxy` and has its own transform applied on top on
receive/view, so seeding the "definition" with one placement's already-placed
geometry double-transforms every placement of that cell. This is invisible
with a single placement (nothing to collide with), and only shows up once
multiple placements of the same shared cell are sent together — exactly the
reported "collapses into tiny or sparse geometry" symptom, and exactly why
single-object sends looked fine while multi-object sends didn't.

## Fix applied

`MicroStationInstanceUnpacker.cs` (`UnpackSharedCell`): resolve
`FindSharedCellDefinition` first. If it fails, log a warning and return
without proxying — the element still gets added as a plain atomic object by
`UnpackSelection`, so it converts as ordinary (non-instanced) placed geometry
via the standard element converter instead of corrupting the shared
definition for every sibling placement. Also removed the now-stale "falling
back to placement geometry" log message in `FindSharedCellDefinition`'s catch
block, since that fallback no longer exists.

## Risks & Open Questions
- Fix is reasoned from the instancing invariant (definition geometry must be
  placement-independent) and matches the existing pattern used elsewhere in
  this repo (AutoCAD blocks / Revit families) — not verified against a live
  MicroStation SDK build, since the Bentley assemblies are Windows-only and
  unavailable in this dev environment. Needs validation on Windows with an
  actual DGN file containing multiple placements of the same shared cell.
- Whether `EnsureSharedCellDefinitionsIndexed`'s source
  (`_settingsStore.Current.Model.GetElements()`) reliably contains
  `SharedCellDefinitionElement`s for all files is unconfirmed — if it
  systematically misses them, more shared-cell sends will now take the
  non-instanced fallback path (correct geometry, but flattened instead of a
  true Speckle instance). That would be a separate, follow-up improvement to
  the definition lookup itself, not a regression from this fix.
- Non-shared (`CellHeaderElement`) cells were not affected — each placement
  already gets its own one-off definition, so there's no cross-placement
  sharing to corrupt.

**Progress**: 80% [████████░░]

**Last Updated**: 2026-07-28 19:10:00

## 📝 Plan Steps
- ✅ **Inspect the MicroStation send entry point and root object builder**
- ✅ **Inspect document/model store and selection aggregation behavior**
- ✅ **Identify the single-vs-multiple transfer mismatch**
- ✅ **Patch the smallest shared send path responsible for aggregation**
-  **Build the solution to validate the fix** (blocked: Bentley SDK is Windows-only, unavailable in this Linux environment — needs validation on Windows)

