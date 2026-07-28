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

## Risks & Open Questions
- The current multi-object failure could be caused by a grouping or transform bug, not just aggregation logic.
- `MicroStationDocumentModelStore.cs` may be a red herring if the issue is actually in send selection filtering or root object construction.
- Need to avoid regressing the already-working single-object case.

**Progress**: 20% [██░░░░░░░░]

**Last Updated**: 2026-07-28 18:18:35

## 📝 Plan Steps
- ✅ **Inspect the MicroStation send entry point and root object builder**
- 🔄 **Inspect document/model store and selection aggregation behavior**
-  **Identify the single-vs-multiple transfer mismatch**
-  **Patch the smallest shared send path responsible for aggregation**
-  **Build the solution to validate the fix**

