# Bentley connectors (v3 / next-gen)

Next-generation Speckle connectors for Bentley applications. These are new v3 connectors built on
the DUI3 / `Speckle.Sdk` architecture — they are **not** ports of the legacy (v2) community connectors
that lived in [`specklesystems/speckle-sharp`](https://github.com/specklesystems/speckle-sharp), though
the converter geometry math is derived from that work (originally authored by Arup).

## Status

| App | Versions targeted | Send | Receive | Closest analog in this repo |
| --- | --- | --- | --- | --- |
| **MicroStation** | 2026 | ✅ | ✅ | AutoCAD |
| **OpenRoads Designer** | 2026 | ✅ (civil send) | ⚙️ civil rebuild scaffolded | Civil 3D |
| **OpenRail Designer** | 2026 | ✅ (civil send) | ⚙️ civil rebuild scaffolded | Civil 3D |
| OpenBuildings Designer | 2024 | planned | planned | Revit |

**MicroStation 2026** is implemented (send + receive). **OpenRoads/OpenRail 2026** have their civil
**converter** layer scaffolded (`Converters/Bentley/Speckle.Converters.OpenRoadsShared`); the connector
wiring (plugin bootstrap per vertical, civil model service, civil send path) is the next step — see below.
**OpenBuildings** is still planned.

## Layout

Mirrors the rest of the repo: a shared project holds all logic, thin per-version projects set the
target framework, Bentley API references and a `#define`.

```
Connectors/Bentley/
  Speckle.Connectors.MicroStationShared/   # shared connector code (bindings, store, plugin, operations)
  Speckle.Connectors.MicroStation2026/     # net8.0-windows, references MicroStation 2026 assemblies
Converters/Bentley/
  Speckle.Converters.MicroStationShared/   # shared converter code (to/from Speckle)
  Speckle.Converters.MicroStation2026/
Speckle.MicroStation.slnx
```

## Bentley SDK references (local)

Bentley's managed API assemblies are **not redistributable** and there is no public NuGet package, so
the projects reference DLLs from a local install. The install directory defaults to:

```
%ProgramW6432%\Bentley\MicroStation 2026\MicroStation\
```

Override with an MSBuild property or environment variable if your install lives elsewhere:

```
dotnet build Speckle.MicroStation.slnx -p:MicroStationInstallDir="D:\Bentley\MicroStation 2026\MicroStation\"
```

Referenced assemblies (`<Private>false</Private>`, i.e. not copied to output — they are loaded by the host):
`Bentley.MstnPlatformNET`, `Bentley.DgnPlatformNET`, `Bentley.GeometryNET`, `Bentley.GeometryNET.Structs`,
`Bentley.ECObjects`, `Bentley.General.1.0`.

> Because these are local references, CI cannot restore/build these projects without the Bentley SDK
> present. They are deliberately kept out of the main solutions. A future improvement is publishing
> internal reference-assembly NuGet packages (as the Autodesk connectors do via `Speckle.*.API`).

## Deploying / debugging (MicroStation)

1. Build `Speckle.Connectors.MicroStation2026`.
2. Copy the build output to a folder on `MS_ADDINPATH` (see `Speckle2MicroStation.cfg`).
3. Register the add-in via `MS_DGNAPPS > Speckle.Connectors.MicroStation2026`.
4. In MicroStation, run the **`Speckle`** keyin to open the panel.

## Data model

Elements are sent as `DataObject`s: a `displayValue` (Speckle geometry) plus a `properties` dictionary
extracted from the element's EC (Engineering Content) instance data, grouped per level in the output
collection. This matches the modern v3 approach used by Tekla/TSD/CSi rather than emitting per-discipline
schema objects.

Units follow MicroStation's UoR (Units of Resolution) scheme: native coordinates are divided by
`UorPerMaster` on the way to Speckle and multiplied on the way back.

### Multiple models per file

A DGN file is a container of many models — design models (2D or 3D), drawing models and sheet models. The
connector operates on the **active model** (whatever the user is currently in), and treats each model as a
distinct document:

- The root collection is named after the active model and tagged with `fileName`, `modelName`, `modelType`
  (Design/Drawing/Sheet, as reported by the host) and `dimension` (`2D`/`3D`).
- `GetDocumentInfo` identifies the document as `file + active model`, so switching models is a distinct
  document in the UI, with its own model cards.
- Because element ids are only unique **within** a model, model cards are stored **per model**: the DGN's EC
  state property holds a `{ modelKey: cardsJson }` envelope keyed by model id, so different models in the same
  file never cross-contaminate each other's selections.

> Follow-ups: reacting to a live active-model switch inside an open panel needs a model-activation event wired
> to `MicroStationDocumentModelStore.OnDocumentSwap` (the method exists; the event is not wired yet). Sending a
> model other than the active one, and sheet/drawing-specific concerns (borders, annotation scale, the sheet's
> own references) are not specifically handled beyond converting whatever graphic elements are selected.

### Cells (MicroStation's blocks)

Cells are **not** flattened — they go through the same instance-proxy system Speckle uses for AutoCAD
blocks / Revit families, so they round-trip as instances and interop cleanly with other connectors
(`MicroStationInstanceUnpacker` on send, `MicroStationInstanceBaker` on receive):

| Cell type | Native element | Speckle mapping |
| --- | --- | --- |
| **Shared cell** | `SharedCellElement` → `SharedCellDefinitionElement` | True instancing: many `InstanceProxy` placements referencing one shared `InstanceDefinitionProxy` (keyed by definition name). |
| **Normal / orphan cell** | `CellHeaderElement` | One `InstanceProxy` + a one-off `InstanceDefinitionProxy` keyed by the cell's element id (not shared). |
| **Parametric cell** | `SharedCellElement` + parameters | Shared-cell path, with the parameter/variable values captured on the instance proxy's `properties`. |

A cell's placement transform is read via the element graphics processor's `AnnounceTransform` callback and
stored as a column-dominant `Matrix4x4` (translation in master units). Nested cells recurse, preserving
`maxDepth` so receive bakes definitions before the instances that depend on them.

> The transform math (`MicroStationTransformHelper`) and native shared-cell creation
> (`MicroStationInstanceBaker.CreateDefinition` / `CreateInstance`) are the surfaces most likely to need
> adjustment against a live MicroStation SDK build; they are deliberately isolated so any fix stays local.
> Purging previously-baked cell definitions on re-receive is a follow-up.

### References (attached models) — opt-in

Unlike AutoCAD Xrefs (a single block reference), MicroStation reference elements are individually selectable
and snappable as if they were in the active model — they are just read-only until the reference is activated.
The connector uses this:

- A per-model-card **"Include Reference Data"** send setting (`IncludeReferencesSetting`, off by default).
- Each selected reference element carries its own `DgnModelRef` (a `DgnAttachment`), so it is captured with a
  composite application id `R{attachmentElementId}:{elementId}` that distinguishes it from active-model
  elements (plain numeric ids) — see `MicroStationReferenceService`.
- On send, reference ids are dropped unless the setting is on; when on, they are resolved back through the
  attachment (`attachment.GetDgnModel().FindElementById(...)`) and converted like any other element.
- Highlighting selects reference elements against their own attachment model ref.

**Source structure is preserved.** The sent collection mirrors the DGN hierarchy: active-model elements are
grouped by level directly under the root, and each reference's elements are grouped by level under a
per-reference collection named after the source file (e.g. `x.dgn`, tagged `isReference`). Reference element
levels are read from the reference model's own level cache, not the active model's.

```
root (active file)
├── <level>            ← active-model elements
├── x.dgn              ← reference collection (isReference)
│   └── <level>        ← reference elements, by their own levels
└── y.dgn
    └── <level>
```

> MVP scope: top-level attachments only (nested references are a follow-up), and reference geometry is
> converted in the active model's units — references authored with different master units or a scaled/rotated
> attachment transform need per-reference settings / transform application (a follow-up). Attachment
> enumeration/resolution is a Bentley-API-dependent surface, isolated in `MicroStationReferenceService`.

## OpenRoads / OpenRail (civil)

OpenRoads Designer and OpenRail Designer are MicroStation-based verticals that add the `Bentley.CifNET.*`
civil SDK. The connectors reuse the MicroStation geometry base and add civil converters.

- **Converters** (`Speckle.Converters.OpenRoadsShared`, shared by both verticals): Alignment / Profile /
  Corridor / Feature → `DataObject` (display curves reused from the MicroStation curve converter + civil
  properties), following the Civil 3D connector's `DataObject` approach rather than legacy schema objects.
  The version projects import the MicroStation **and** civil shared projitems into one assembly, so the
  MicroStation converter scan registers the civil converters into the same converter manager.
- **CifNET enumeration** (for the civil send, connector side — next step): the active geometric models are
  reached via `ConsensusConnectionEdit.GetActive().GetAllGeometricModels()`, then `model.Alignments` /
  `Corridors` / features are enumerated (as in the ATRL/Atom ORD code). Civil entities are resolved via the
  converter manager directly, because the MicroStation root converter only handles native `Element`s.
- **Civil receive / rebuild** (`ToHost`): received Alignment/Corridor DataObjects are regenerated **natively**
  via the CifNET edit/transient API (`CreateAlignmentByLinearElement`, `CreateProfileByProfileElement`,
  `CreateCorridorByAlignment`, wrapped in `StartTransientMode`/`PersistTransients`) — not baked as dumb
  geometry. A `CivilHostRebuilder` (no-op for plain MicroStation) dispatches them from the receive host
  builder.
- **Faithful geometry round-trip.** Rebuilding an alignment from *display/stroked* curves loses the true arcs
  and spirals — a known pitfall that breaks rebuild. So send extracts **structured** horizontal geometry
  (lines/arcs/spirals + parameters) into the shared
  [`AlignmentGeometrySchema`](../../Sdk/Speckle.Converters.Common/Civil/AlignmentGeometrySchema.cs), and
  receive rebuilds from that (`Line.Create` / `CircularArc.Create3` / `Spiral.Create1`). The parametric
  **read** is the fragile part, so it is fully defensive and display curves are always attached as a
  visualization + fallback. Profile vertical-curve structure and corridor template drops / point controls /
  superelevation are the remaining data to model on the schema.
- **Still to refine**: OpenRail rail-specifics (cant/turnouts) behind the `OPENRAIL` define; corridor
  template-drop / point-control / superelevation rebuild; and validating every CifNET call against a live SDK.

### Corridor interoperability (goal)

The target is to send a corridor's full **definition** to Speckle and rebuild it — in ORD, or in **Civil 3D**
for true ORD↔C3D interop. The way to get there is a **connector-neutral corridor schema** that **both** the
OpenRoads and Civil 3D converters map to on send and from on receive.

That schema now lives in the shared SDK as
[`Speckle.Converters.Common.Civil.CorridorSchema`](../../Sdk/Speckle.Converters.Common/Civil/CorridorSchema.cs)
so both connectors reference the exact same keys. A corridor is a `DataObject` with `type = "Corridor"`; its
`displayValue` always carries the geometry (so non-civil consumers see the model), and its `properties` carry
the definition: `baseline` (nested `alignment` + `profile` DataObjects), `startStation`/`endStation`,
`keyStations`, `templateDrops`, `pointControls`, `superelevation` (incl. rail `cant`) and `surfaces`.

The corridor converter already emits the baseline (alignment + active profile, nested as their own converted
DataObjects), key stations and surface names against this schema; template drops / point controls /
superelevation are the next data to populate — and the Civil 3D converter mapping to the same
`CorridorSchema` keys is what closes the ORD↔C3D loop.

## Notes for the remaining connector

- **OpenBuildings** — primary workflow is **send/receive with Revit**, so align its data model and
  property extraction with the **Revit** connector's `DataObject` shape to keep round-trips clean. It adds
  the `Bentley.Building.Api` assemblies for grids and building elements.

## Enhancements incorporated from Atom.Platform

Proven Bentley patterns from the author's `Atom.Platform` (ATRL) work have been folded in:

- **Level baking on receive** (`MicroStationLevelBaker`): create levels + `FileLevelCache.Write()` once, and
  assign elements via `ElementPropertiesSetter.SetLevel().Apply()` (since `Element.LevelId` is getter-only).
- **CifNET enumeration** pattern (`ConsensusConnectionEdit.GetAllGeometricModels()`) informs the civil send.
- Earmarked for follow-up: item-type property attachment via `CustomItemHost` / `ItemTypeLibrary`, RGB
  element colour via `AddRgbColorAttribute`, and `ElementCopyContext` for reference activation.
