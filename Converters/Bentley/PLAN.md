# MicroStation Solid/Surface/Mesh/Parametric-Solid/Cell Conversion — Implementation Plan

Status as of 2026-07-24, for work planned the week of 2026-07-27.

## Decision

Commit `1d3adfb` ("feat(microstation): robust fallback for unsupported elements")
made `SolidElementToSpeckleConverter` and `SurfaceElementToSpeckleConverter`
delegate to the new `ElementToSpeckleFallbackConverter`. That converter
produces a generic `DataObject` (display mesh + a `properties` metadata bag)
rather than a typed Speckle geometry object. This is the wrong long-term shape
for element types we already know how to handle: Solid, Surface, Mesh, and
Parametric Solid should each get their own dedicated `ToSpeckle`/`ToHost`
converter, per this repo's converter architecture (small composable
`ITypedConverter<TIn, TOut>` per type, dispatched via
`[NameAndRankValue]`). `ElementToSpeckleFallbackConverter` should remain only
as the last-resort catch-all for genuinely unsupported element types.

## Current state (verified against `Converters/Bentley` and
`Connectors/Bentley` on `claude/speckle-conversion-plan-vd7g97`)

- `Speckle.Converters.MicroStationShared/ToSpeckle/TopLevel/`
  - `MeshHeaderElementToSpeckleConverter` — **correct pattern**. Uses a real
    `ITypedConverter<BG.PolyfaceHeader, SOG.Mesh>` chain to produce an actual
    `SOG.Mesh`.
  - `SolidElementToSpeckleConverter` / `SurfaceElementToSpeckleConverter` —
    currently thin wrappers that just call
    `ElementToSpeckleFallbackConverter.Convert(target)`. **This is what we're
    replacing.**
  - `ElementToSpeckleFallbackConverter` — `[NameAndRankValue(typeof(BDE.Element),
    SPECKLE_DEFAULT_RANK - 100)]`, lowest-rank catch-all. Extracts curves/facet
    meshes via `ElementGraphicsOutput.Process`, returns a `DataObject` with
    `properties` including `sourceElementClass`, `sourceMSElementType`,
    `isParametricSolidType`. No dedicated Parametric Solid converter exists —
    it's just this boolean flag.
- `Speckle.Converters.MicroStationShared/ToHost/Geometry/`
  - `SolidLikeDataObjectToHostConverter` — `[NameAndRankValue(typeof(DataObject),
    SPECKLE_DEFAULT_RANK + 1)]`, only accepts fallback `DataObject`s whose
    `sourceMSElementType` is `Solid`/`Surface`/`Cone`, rebuilds display
    geometry only (no real brep reconstruction).
  - `DisplayableObjectConverter` — generic ToHost fallback baking any
    `DisplayableObject`'s display value.
- Cells are **not** run through the geometry-converter path at all today —
  `Connectors/Bentley/Speckle.Connectors.MicroStationShared/HostApp/
  MicroStationInstanceUnpacker.cs` (`UnpackSharedCell`, `UnpackCell`) and
  `MicroStationInstanceBaker.cs` handle Shared/Normal/Parametric cells as
  `InstanceProxy`/`InstanceDefinitionProxy`, which is the correct
  instance-preserving pattern per this repo's Speckle integration guidance.
  What's unconfirmed is whether the *elements nested inside* a cell
  definition (its Solid/Surface/Mesh/Parametric Solid members) are dispatched
  through the standard top-level converter registry (so they'd automatically
  benefit from the fixes below) or short-circuited to the fallback.
- Reference pattern from another connector for the target shape: AutoCAD's
  `Converters/Autocad/Speckle.Converters.AutocadShared/ToSpeckle/Geometry/
  Solid3dToSpeckleConverter.cs` — typed `SOG.SolidX` with a lossless raw
  encoding, a display mesh, and computed volume/area, not a metadata dump.
- No test project exists yet for `Speckle.Converters.MicroStationShared`.

## Plan

### 1. Spike: encoding strategy (do first, blocks everything else)
- Determine whether MicroStation's API exposes a brep/solid kernel object
  (Parasolid handle, `ISolidKernelEntity`, `IBRepEntity`, or similar) that can
  be serialized for lossless round-trip, analogous to AutoCAD's `ABR.Brep` +
  SAT encoding.
- Decide the target Speckle type per element:
  - `SolidElement` → `SOG.Solid` / `SOG.SolidX` (raw encoding if available,
    else mesh-only degraded fallback — decide explicitly, don't let it fall
    through to the generic element fallback).
  - `SurfaceElement` → `SOG.Surface` (or `SOG.SolidX` if that's the closer
    fit for MicroStation's surface representation).
  - `MeshHeaderElement` → unchanged, already correct.
  - Parametric Solid → same target type as Solid, plus driving parameters
    captured in `properties` (not just a boolean flag).
- Trace `MicroStationInstanceUnpacker.UnpackSharedCell`/`UnpackCell` to
  confirm whether definition-child elements dispatch through the standard
  converter registry. Document the answer here before starting step 3.

### 2. Typed ToSpeckle converters (Solid, Surface, Parametric Solid)
- Add `ITypedConverter<BDE.SolidElement, SOG.Solid>` (or `SolidX`) under
  `ToSpeckle/Raw/`; rewrite `SolidElementToSpeckleConverter` to use it instead
  of delegating to the fallback.
- Same for `SurfaceElementToSpeckleConverter` → real `SOG.Surface`.
- Add a new `ParametricSolidElementToSpeckleConverter` (doesn't exist today)
  that captures driving parameters into `properties`, keeping
  `displayValue` for visible geometry, per this repo's "host-specific
  semantics in properties" convention.

### 3. ToHost mirror
- Narrow `SolidLikeDataObjectToHostConverter` so it only handles genuinely
  fallback `DataObject`s — Solid/Surface should no longer round-trip through
  it once typed encoding exists.
- Add typed `ToHost/Raw/` converters: `SOG.Solid → BDE.SolidElement`,
  `SOG.Surface → BDE.SurfaceElement`, using whichever kernel-entity creation
  API the spike identified. If brep reconstruction isn't achievable, document
  the degraded (mesh-rebuild) path explicitly rather than silently reusing
  the generic fallback converter.

### 4. Cell integration
- If cell-definition children already dispatch through the standard
  converter registry: verify end-to-end (send a Shared Cell containing a
  Solid/Surface/Mesh/Parametric Solid, confirm the received Speckle object
  graph has typed geometry, not fallback `DataObject`s).
- If they don't: fix `MicroStationInstanceUnpacker` /
  `MicroStationHostObjectBuilder` to route definition-member elements through
  the same top-level converter dispatch as top-level elements.

### 5. Tests
- Add a `Speckle.Converters.MicroStationShared.Tests` project (NUnit4, Moq,
  AwesomeAssertions, per repo convention — none exists today for this
  converter project).
- Unit test each new/changed converter: `SolidElementToSpeckleConverter`,
  `SurfaceElementToSpeckleConverter`, `ParametricSolidElementToSpeckleConverter`,
  the new ToHost raw converters, and the narrowed
  `SolidLikeDataObjectToHostConverter`.
- Run `./build.sh format` and `./build.sh test` before pushing. Full
  build/test needs Windows (MicroStation SDK is Windows-only) — plan Windows
  time for final verification, not just the Linux dev loop.

## Division of labor

| Task | Owner |
|---|---|
| Brep/kernel API research, encoding format decision | You |
| Cell-dispatch trace/fix | You |
| Typed ToSpeckle converters (Solid/Surface/Parametric) | Copilot draft → you review |
| Typed ToHost converters | You (kernel calls) + Copilot (wiring) |
| Fallback converter scope narrowing | You (small, deliberate diff) |
| Unit tests | You (1 template) → Copilot (rest) |

## Risk

If MicroStation has no accessible brep/kernel serialization API, lossless
round-trip for Solid/Surface isn't achievable, and the honest target is
mesh-only `SOG.Mesh`/`SOG.Solid` with a tessellated `displayValue`. Decide
this on day 1 of the spike rather than discovering it mid-week.
