# MicroStation Solid/Surface/Mesh/Parametric-Solid/Cell Conversion & Item Type Properties — Implementation Plan

Status as of 2026-07-25, for work planned the week of 2026-07-27.

Scope added 2026-07-25: Item Type property round-trip — export each element's
Item Type property values to Speckle `properties`, and write Speckle
`properties` back onto Item Types on receive. This was already earmarked as
follow-up work (`Connectors/Bentley/README.md:211-212`, "item-type property
attachment via `CustomItemHost` / `ItemTypeLibrary`"); it's now in scope for
this sprint alongside the Solid/Surface/Mesh/Parametric-Solid/Cell work.

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
- Item Types (verified against current repo state):
  - No Item Type code exists yet. It's a documented TODO only
    (`Connectors/Bentley/README.md:211-212`).
  - What exists today is generic EC (Engineering Content) property reading,
    not Item Types specifically: `Speckle.Converters.MicroStationShared/
    ToSpeckle/Properties/PropertiesExtractor.cs:15-97` calls
    `DgnECManager.Manager.GetElementProperties()` and walks
    `IDgnECInstance`/`IECPropertyValue` into a flat
    `Dictionary<string, object?>`, grouped by EC class display label. It does
    not distinguish Item Type instances from other EC classes on the element,
    so Item Type values, if present, are currently mixed in undifferentiated
    with everything else EC exposes.
  - `MicroStationRootToSpeckleConverter.cs:44-59` is where
    `PropertiesExtractor` output gets merged into `DataObject.properties` for
    every element, after the type-specific converter runs.
  - There is no reverse path today. The only existing "write host metadata
    back onto an element" pattern is level assignment:
    `MicroStationLevelBaker.cs:70-83`
    (`Connectors/Bentley/Speckle.Connectors.MicroStationShared/HostApp/`)
    uses `new ElementPropertiesSetter().SetLevel(levelId).Apply(element)`
    because `Element.LevelId` is getter-only — the same shape (a dedicated
    `*Setter`/`*Baker` invoked during the host object build) is the template
    for an Item Type writeback.

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

### 6. Item Type properties: export to Speckle, import from Speckle

- **Spike**: confirm how `ItemTypeLibrary`/`CustomItemHost` expose Item Type
  definitions and instance values on a `DgnElement` (vs. the generic EC
  classes `PropertiesExtractor` already reads), and whether Item Type
  instances are already showing up — undifferentiated — inside today's
  `PropertiesExtractor` output or are missed entirely. Confirm on day 1;
  it determines whether step 2 below is "re-tag existing data" or "add a new
  read path".
- **ToSpeckle (export)**: add a dedicated Item Type read path — either a new
  `ItemTypePropertiesExtractor` or an extension to
  `PropertiesExtractor.cs:15-97` — that reads each element's `CustomItemHost`
  Item Type instances and writes them into `DataObject.properties` under a
  distinct, namespaced key (e.g. `properties["Item Types"]["<Item Type
  name>"]`) rather than flattened in with generic EC data, so downstream
  consumers can tell Item Type values apart from other properties. Wire it
  into `MicroStationRootToSpeckleConverter.cs:44-59` alongside the existing
  `PropertiesExtractor` call.
- **ToHost (import)**: add a new baker, e.g. `MicroStationItemTypeBaker`,
  following the `MicroStationLevelBaker.cs:70-83` /
  `ElementPropertiesSetter` template — read the namespaced Item Type bag back
  out of the received `Base`'s `properties`, resolve or create the matching
  `ItemTypeLibrary` Item Type definition on the target element via
  `CustomItemHost`, and set each property value. Decide and document the
  behavior when an Item Type named in the incoming data doesn't exist in the
  target file (create it vs. skip with a warning — don't silently drop data).
  Invoke it from `MicroStationInstanceBaker.cs` alongside the level baker.
- **Tests**: cover the new extractor and baker in the
  `Speckle.Converters.MicroStationShared.Tests` project added in step 5,
  including the round-trip (send an element with Item Type values, receive
  it, confirm the same Item Type/values land on the rebuilt element).

## Division of labor

| Task | Owner |
|---|---|
| Brep/kernel API research, encoding format decision | You |
| Cell-dispatch trace/fix | You |
| Typed ToSpeckle converters (Solid/Surface/Parametric) | Copilot draft → you review |
| Typed ToHost converters | You (kernel calls) + Copilot (wiring) |
| Fallback converter scope narrowing | You (small, deliberate diff) |
| Unit tests | You (1 template) → Copilot (rest) |
| Item Type API spike (`ItemTypeLibrary`/`CustomItemHost`) | You |
| Item Type export (ToSpeckle extractor) | Copilot draft → you review |
| Item Type import (`MicroStationItemTypeBaker`) | You (API calls) + Copilot (wiring) |
| Item Type round-trip tests | You (1 template) → Copilot (rest) |

## Risk

If MicroStation has no accessible brep/kernel serialization API, lossless
round-trip for Solid/Surface isn't achievable, and the honest target is
mesh-only `SOG.Mesh`/`SOG.Solid` with a tessellated `displayValue`. Decide
this on day 1 of the spike rather than discovering it mid-week.

Item Types add a second, independent risk: `CustomItemHost`/`ItemTypeLibrary`
is unverified territory (no code in this repo touches it yet, per the README
TODO). Creating Item Type definitions that don't already exist in the
receiving file may require schema/library setup beyond a per-property API
call. If definition creation on receive turns out to be unsupported or unsafe
to do implicitly, the honest fallback is import-only-if-already-defined
(skip + warn otherwise) rather than silently failing or corrupting the file's
Item Type libraries — decide and document this during the spike, same as the
brep/kernel risk above.
