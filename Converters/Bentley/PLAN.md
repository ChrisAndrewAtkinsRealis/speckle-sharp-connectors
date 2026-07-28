# MicroStation Solid/Surface/Mesh/Parametric-Solid/Cell Conversion & Item Type Properties — Implementation Plan

Status as of 2026-07-25, for work planned the week of 2026-07-27.

Scope added 2026-07-25: Item Type property round-trip — export each element's
Item Type property values to Speckle `properties`, and write Speckle
`properties` back onto Item Types on receive. This was already earmarked as
follow-up work (`Connectors/Bentley/README.md:211-212`, "item-type property
attachment via `CustomItemHost` / `ItemTypeLibrary`"); it's now in scope for
this sprint alongside the Solid/Surface/Mesh/Parametric-Solid/Cell work.

Also added 2026-07-25: `ExtendedElementsElement`-classed objects currently
have no conversion path that reliably succeeds — see the "Current state" and
plan §7 below.

## Progress Tracker

**Last updated**: 2026-07-28 (second pass, this session), replacing the
reflection-based §6 Item Type read/write with the real `CustomItemHost` API.
Most of §1-§2, §6 and §7 landed in commit `6757fbc` ("20260728", Chris
Andrew, 2026-07-28) without this file being updated — the "Current state"
section above and the plan below are now stale in places; this tracker is the
up-to-date source of truth. `Connectors/Bentley/report.json` and
`ELEMENT_COVERAGE.csv` (added in the same commit) are real element-inventory
dumps from a live Windows/MicroStation run — evidence the §1 spike did happen
against a real file, even though the encoding *decision* itself (mesh-only,
no lossless kernel encoding — see §1 row below) isn't written down anywhere
in prose.

**Overall: ~70%** [███████░░░]

| # | Item | Status | Notes |
|---|---|---|---|
| 1 | Spike: encoding strategy | ✅ Done (decided) | No accessible brep/kernel serialization API is used — `SOG.SolidX.encodedValue` is always written with `format = "microstation"` and `contents = string.Empty` (`ElementToSpeckleDataObjectBuilder.ConvertToSolidX`). Decision taken implicitly: mesh-only tessellated `displayValue`, per the plan's own documented fallback risk. Not written down in this file until now. |
| 1b | Cell-dispatch trace | ✅ Done (confirmed) | Cell-definition children are added as plain atomic objects (`MicroStationInstanceUnpacker.UnpackDefinition` → `AddAtomicObject`) and flow through the same `ConvertOrProxy`/converter-registry path as top-level elements (`MicroStationRootObjectBuilder.Build`). They already benefit from the typed Solid/Surface converters below with no extra wiring needed. |
| 2 | Typed ToSpeckle converters (Solid) | ✅ Done | `SolidElementToSpeckleConverter` → `SolidElementToSpeckleRawConverter` → `ElementToSpeckleDataObjectBuilder.ConvertToSolidX`, produces a real `SOG.SolidX` (tessellated mesh `displayValue`, empty raw encoding, primitive parameter values in `properties`). **Was not actually compiled** until this pass — see "Fixed" below. |
| 2 | Typed ToSpeckle converters (Surface) | ✅ Done | `SurfaceElementToSpeckleConverter` → `SurfaceElementToSpeckleRawConverter` → `ElementToSpeckleDataObjectBuilder.ConvertToSurfaceOrGraphicOrDataObject`, produces a real `SOG.Surface` (NURBS control points/knots) when the graphics processor announces an `MSBsplineSurface`, else falls back to a graphic/DataObject. This is actually *better* than the plan's minimum bar (a real parametric surface, not mesh-only). |
| 2 | Typed ToSpeckle converter (Parametric Solid) | ✅ Done (folded in, this pass) | Plan called for a *separate* `ParametricSolidElementToSpeckleConverter`, but the converter-registration framework only allows one top-level converter per exact .NET type (`ConverterManager.ResolveConverter`/`AddConverters` — confirmed by reading `Sdk/Speckle.Converters.Common/Registration/*.cs`), so a second converter on `BDE.SolidElement` can't coexist with `SolidElementToSpeckleConverter`. Folded the decision into the single reachable converter instead: added `ElementToSpeckleDataObjectBuilder.IsParametricSolid(...)` and had `SolidElementToSpeckleRawConverter` call it to decide `includeParametricValues`. Deleted the dead, never-referenced `ParametricSolidElementToSpeckleRawConverter.cs`, which also implemented `ITypedConverter<BDE.SolidElement, Base>` and would have created an ambiguous duplicate DI registration for that interface. |
| 3 | ToHost mirror: narrow fallback converter | ✅ Done | `SolidLikeDataObjectToHostConverter.IsSolidLikeFallback` now gates on `conversionKind == "fallback"`, so it no longer intercepts the new typed Solid/Surface output. |
| 3 | ToHost mirror: typed `SOG.SolidX → BDE` converter | ✅ Done, this pass | **Was completely missing** — receiving a MicroStation-sent Solid back into MicroStation would have thrown `ConversionNotSupportedException` (no converter registered for `SOG.SolidX`, which does not inherit `DisplayableObject`; confirmed against the equivalent AutoCAD `SolidXToHostConverter`, the only other place in this repo that handles `SolidX`). Added `ToHost/Geometry/SolidXToHostConverter.cs`: rebuilds from the tessellated `displayValue` meshes (there is no lossless raw encoding to decode per §1), mirroring the AutoCAD converter's fallback branch. |
| 3 | ToHost mirror: typed `SOG.Surface → BDE.SurfaceElement` converter | ❌ Not started | No connector in this repo has a `SOG.Surface` ToHost converter yet (checked: zero matches for `typeof(SOG.Surface)` repo-wide), so there's no established pattern to follow and native B-spline surface reconstruction is real, unverified Bentley SDK territory. Left alone rather than guessed at — needs the Windows spike. Received Surfaces currently have no receive path at all (no fallback either, since real `SOG.Surface` isn't a `DisplayableObject`). |
| 4 | Cell integration | ✅ Done (see 1b) | |
| 5 | Tests | 🟡 Started, partial | `Speckle.Converters.MicroStationShared.Tests` project exists (NUnit, `net48`) with one test (`FallbackConverterTests`, covers §7's empty-`displayValue` fix only). It references `Speckle.Converters.MicroStation2026.csproj` directly, so like the rest of this connector it's Windows-SDK-only and can't be built/run from this Linux environment. No coverage yet for the Solid/Surface/Parametric converters, the new `SolidXToHostConverter`, or the narrowed fallback host converter. |
| 6 | Item Type properties: export | 🟡 Rewritten to real API, build-unverified | `ItemTypePropertiesExtractor` no longer reflects for guessed property names. It now constructs `new CustomItemHost(element, false)` and reads `CustomItemHost.CustomItems` (an `IList<IDgnECInstance>`, per the Bentley "Item Types CRUD Operations" managed-API sample) — this differentiates Item Type instances from the generic EC data `PropertiesExtractor` reads, which was the open question from the §6 spike. Each Item Type's values (extracted via the new shared `EcPropertyValueReader`, factored out of `PropertiesExtractor`'s previously-duplicated logic) are written to `properties["Item Types"][<Item Type name>] = { "library": <owning schema/library name>, "properties": {...} }` — the library name is carried alongside the values because the baker needs it to resolve the same Item Type on receive. Still unverified: exact `CustomItemHost` namespace/constructor semantics and whether `ClassDefinition.Schema.Name` is really the library name — no Windows/MicroStation SDK available in this environment to compile-check. |
| 6 | Item Type properties: import | 🟡 Rewritten to real API, build-unverified | `MicroStationItemTypeBaker.ApplyItemTypes` no longer reflects for a settable `"ItemType"` element property. It now reads `DataObject.properties["Item Types"]`, resolves each Item Type via `CustomItemHost.GetCustomItem(library, itemTypeName)`, and writes values with `IDgnECInstance.SetString` + `WriteChanges()` (per the same Bentley CRUD sample: `ecInstance.SetString("Type", "Modified Sofa")`). **Decision** (per the Risk section below): an incoming Item Type with no match in the target file is skipped and logged, not auto-created — creating `ItemTypeLibrary`/`ItemType` definitions generically was judged too risky to guess without SDK verification, so this is the documented "import-only-if-already-defined" fallback the plan called for. The parsing of the `{"library", "properties"}` entry shape was pulled into a pure `MicroStationItemTypeBaker.TryParseItemTypeEntry` so it's unit-testable without the Bentley SDK (see tests below); the `CustomItemHost`/`IDgnECInstance` calls themselves are still unverified against a real file. |
| 6 | Item Type round-trip tests | 🟡 Partial | Added `Speckle.Connectors.MicroStationShared.Tests` (new test project, mirrors the existing Converters test project: NUnit4, net48, references `Speckle.Connectors.MicroStation2026.csproj`) with unit tests for `MicroStationItemTypeBaker.TryParseItemTypeEntry` (well-formed entry, missing/blank library, missing properties, non-dictionary entry). This covers the wire-contract between extractor and baker but not the `CustomItemHost` calls themselves — those need Moq mocks of Bentley EC interfaces (`IDgnECInstance`/`IECClass`/`IECPropertyValue`) whose exact member shapes aren't confirmed, or a live round-trip against a real DGN file. Still blocked on Windows verification for full coverage. |
| 7 | ExtendedElementsElement: don't throw on empty displayValue | ✅ Done | `ElementToSpeckleFallbackConverter.Convert` returns a properties-only `DataObject` when no display geometry is found instead of throwing `ConversionException`. Also gained an additional bounding-box-mesh fallback (`TryAddRangeFallbackMesh`) beyond what the plan asked for. |
| 7 | ExtendedElementsElement: confirm concrete SDK type | ❌ Not started (blocked) | Still needs a Windows box with MicroStation 2026 to confirm the concrete managed type(s) — can't be done from this Linux environment. The practical fix (§7 above) doesn't strictly depend on it, so this is lower priority now. |
| 7 | ExtendedElementsElement: ToHost round-trip decision | ❌ Not started | Not yet explicitly decided/documented whether extended elements round-trip back into the host file on receive. |

### Fixed in this pass (2026-07-28)

1. **`SolidElementToSpeckleRawConverter.cs` was never added to
   `Speckle.Converters.MicroStationShared.projitems`** — it existed on disk
   but wasn't part of the compiled shared-items list, so on a real Windows
   build the type wouldn't exist in the assembly at all. Added it to the
   `.projitems` file.
2. **Ambiguous DI registration**: `ParametricSolidElementToSpeckleRawConverter`
   was a second, unreferenced implementation of
   `ITypedConverter<BDE.SolidElement, Base>` (the same interface
   `SolidElementToSpeckleRawConverter` implements). Multiple registrations for
   one interface resolve non-deterministically via last-registered-wins when
   injected as a single constructor parameter. Deleted it and folded its
   "is this a parametric solid" decision into
   `ElementToSpeckleDataObjectBuilder.IsParametricSolid` +
   `SolidElementToSpeckleRawConverter`, so there's exactly one converter for
   `BDE.SolidElement` and it makes the parametric/non-parametric call itself.
3. **Missing `SOG.SolidX` receive path**: added
   `ToHost/Geometry/SolidXToHostConverter.cs` so a MicroStation-authored Solid
   can round-trip back into MicroStation (degraded to its tessellated display
   mesh, since there's no lossless encoding to decode — consistent with the
   §1 decision).

None of this has been build-verified against the real Bentley SDK (Windows
required, unavailable here) — the `.projitems`/DI fixes are verified by
reading the registration code paths in `Sdk/Speckle.Converters.Common`
directly, and the new converter mirrors an existing, working pattern
(AutoCAD's `SolidXToHostConverter`). Flagging as the next thing to confirm on
Windows.

### §6 Item Type spike + implementation (this session)

Ran the "spike" from the plan as a documentation search, since no Windows/
MicroStation box is available in this environment: found Bentley's own
"Item Types CRUD Operations with Native, COM and Managed APIs" sample and
several MicroStation Programming Forum threads confirming the real managed
API shape used to attach/read/write Item Types on an element:

- `CustomItemHost` (constructed as `new CustomItemHost(element, false)`,
  assumed namespace `Bentley.DgnPlatformNET.Elements` alongside `BDE.Element`
  — **not independently confirmed**, since no forum sample showed the
  `using` statements).
- `CustomItemHost.CustomItems` → `IList<IDgnECInstance>`, every Item Type
  instance attached to the element. This is the answer to the spike's open
  question: Item Type instances *are* reachable independently of the
  generic `DgnECManager.Manager.GetElementProperties(element,
  SearchAllClasses)` walk `PropertiesExtractor` already does, so they can be
  read into their own namespaced bag instead of being mixed in.
- `CustomItemHost.GetCustomItem(libraryName, itemTypeName)` → the specific
  `IDgnECInstance` for one Item Type, or presumably `null` if not applied to
  the element — used on receive to find the write target.
- `IDgnECInstance.SetString(accessString, value)` — the one write method
  directly confirmed in a real code sample
  (`ecInstance.SetString("Type", "Modified Sofa")`). Used for every property
  regardless of its underlying scalar type (numbers formatted
  culture-invariantly via `IFormattable` first); typed setters
  (`SetDouble`/`SetInteger`/etc.) may also exist but weren't confirmed, so
  weren't guessed at.
- `IDgnECInstance.WriteChanges()` — assumed necessary to persist a
  `SetString` call back to the element, per the general DgnEC instance
  pattern; not independently confirmed in a sample.

Implemented with this API:
`Converters/Bentley/Speckle.Converters.MicroStationShared/ToSpeckle/
Properties/ItemTypePropertiesExtractor.cs` (export) and
`Connectors/Bentley/Speckle.Connectors.MicroStationShared/HostApp/
MicroStationItemTypeBaker.cs` (import). Also factored the scalar
EC-value-extraction logic shared by `PropertiesExtractor` and
`ItemTypePropertiesExtractor` into a new
`ToSpeckle/Properties/EcPropertyValueReader.cs` rather than duplicating it.

**Explicit decision** (resolves the plan's Risk section for §6): on receive,
an Item Type named in the incoming data that has no matching definition
already in the target file (i.e. `GetCustomItem` returns nothing) is
**skipped and logged**, not auto-created. Authoring a new `ItemTypeLibrary`/
`ItemType` schema definition generically was judged too risky to implement
without Windows SDK verification — the `ItemTypeLibrary.FindByName` /
`GetItemTypeByName` / `ApplyCustomItem` APIs exist per the forum research,
but their exact parameter semantics (the extra `bool` "import library"
argument seen in samples) weren't confirmed enough to use safely for
writing new definitions into a user's file.

**Still needed before this is trustworthy**: a Windows/MicroStation 2026 box
to (1) confirm `CustomItemHost`'s actual namespace and constructor
semantics, (2) confirm `GetCustomItem` really returns null (vs. throwing)
for an unmatched Item Type, (3) confirm `SetString` performs correct type
coercion for non-string Item Type properties (numeric/boolean), and
(4) run an actual send → receive round-trip against a file with real Item
Types applied. None of this compiles on Linux (same constraint as the rest
of this connector), so it's unverified by anything other than reading the
Bentley sample code found via search — flagging that explicitly rather than
presenting it as done.

**Note on the new test project**: `Connectors/Bentley/Speckle.Connectors.
MicroStationShared.Tests/` has no `packages.lock.json` yet (Central Package
Management + `RestorePackagesWithLockFile` normally require one committed,
per `CLAUDE.md`) — this environment has no `dotnet` CLI available to
generate one. It isn't in any `.slnx`, matching its Converters-side sibling,
so `dotnet restore --locked-mode` on the solution files never touches it;
the `test`/`test-and-pack` Build targets invoke `dotnet test` per
`*.Tests.csproj` without `--locked-mode`, so a missing lock file gets
auto-generated on first restore rather than failing the build. Still, run
`dotnet restore` on it once on Windows and commit the resulting
`packages.lock.json`, to match repo convention.

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
- `ExtendedElementsElement` (verified against current repo state):
  - No dedicated type-specific converter exists. The only related code is a
    metadata-tagging helper, `IsExtendedElementType(string elementTypeName)`
    in `ElementToSpeckleFallbackConverter.cs:172-180`, which string-matches
    `target.ElementType.ToString()` against `MSElementType` names
    (`DgnStoreHeader`, `GroupData`, `Type4`, `Type44`, `DigSetData`,
    `TableEntry`, `View`, `ViewGroup`) and only sets
    `properties["isExtendedElementType"]` — it doesn't change how the element
    is converted.
  - Dispatch isn't the problem: `ConverterManager.cs:13-39` walks .NET base
    types, so anything under `BDE.Element` without its own registered
    converter already reaches `ElementToSpeckleFallbackConverter` (registered
    on `typeof(BDE.Element)`) automatically.
  - The actual gap is `ElementToSpeckleFallbackConverter.cs:67-73`: if no
    curve/mesh display geometry is extracted, it throws a
    `ConversionException` instead of returning a properties-only
    `DataObject`. Extended element types are typically non-graphical
    (schema/data-carrying opcodes, not drawable geometry), so they are the
    elements most likely to hit exactly this path and **fail conversion
    outright**. That contradicts this repo's own guidance (`CLAUDE.md`:
    "Support unsupported host elements with a fallback `DataObject`
    converter that preserves display geometry and metadata rather than
    silently dropping them").
  - Bentley SDK assemblies aren't available in this Linux dev environment —
    `Converters/Bentley/Speckle.Converters.MicroStation2026/
    Speckle.Converters.MicroStation2026.csproj:17-50` resolves them via a
    Windows-only `HintPath` into a local MicroStation 2026 install. Confirming
    `ExtendedElementsElement`'s concrete managed shape and EC/property
    surface needs a Windows box with MicroStation 2026 installed — don't
    guess it from memory.

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

### 7. ExtendedElementsElement conversion

- **Spike (Windows, SDK required)**: confirm the concrete managed type(s)
  behind "extended element" objects (`Bentley.DgnPlatformNET.Elements.
  ExtendedElementsElement` or whichever `MSElementType` values apply — start
  from the `IsExtendedElementType` list in
  `ElementToSpeckleFallbackConverter.cs:172-180`), and confirm whether they
  carry EC/Item Type data (relevant to §6 above) despite having no display
  geometry.
- Fix `ElementToSpeckleFallbackConverter.Convert` (`:25-93`) so extended/
  non-graphical element types don't throw when `displayValue.Count == 0` —
  for these, empty display geometry is expected, not a failure. Return a
  properties-only `DataObject` (empty `displayValue`) instead of raising
  `ConversionException`; keep the exception for element types that should
  have geometry but genuinely failed extraction.
- Decide whether that fix lives inside the shared fallback converter
  (cheapest, and correct if extended elements stay purely metadata-only) or
  becomes a dedicated `ExtendedElementsElementToSpeckleConverter` with its
  own `[NameAndRankValue]` registration, if the concrete type gets confirmed
  and its handling needs to diverge further (e.g. once it also extracts Item
  Type data from §6).
- ToHost: explicitly decide whether extended elements round-trip back into
  the host file on receive, or are send-only/reference metadata — confirm
  this during the spike rather than leaving it an unstated assumption.
- **Tests**: add to `Speckle.Converters.MicroStationShared.Tests` — a
  non-graphical extended element must produce a `DataObject` with properties
  and empty `displayValue`, not throw.

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
| ExtendedElementsElement spike (Windows/SDK) | You |
| Fallback-converter empty-`displayValue` fix | You (small, deliberate diff) |
| ExtendedElementsElement tests | Copilot draft → you review |

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

`ExtendedElementsElement` adds a smaller, contained risk: the fix is scoped
to `ElementToSpeckleFallbackConverter`'s empty-`displayValue` branch, but its
spike needs a Windows machine with MicroStation 2026 installed to inspect the
real SDK type — this can't be verified from the Linux dev loop. Confirm the
concrete type/behavior before writing the fix, not after.
