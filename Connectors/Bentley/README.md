# Bentley connectors (v3 / next-gen)

Next-generation Speckle connectors for Bentley applications. These are new v3 connectors built on
the DUI3 / `Speckle.Sdk` architecture — they are **not** ports of the legacy (v2) community connectors
that lived in [`specklesystems/speckle-sharp`](https://github.com/specklesystems/speckle-sharp), though
the converter geometry math is derived from that work (originally authored by Arup).

## Status

| App | Versions targeted | Send | Receive | Closest analog in this repo |
| --- | --- | --- | --- | --- |
| **MicroStation** | 2026 | ✅ (initial) | ✅ (initial) | AutoCAD |
| OpenRoads Designer | 2026 | planned | planned | Civil 3D |
| OpenRail Designer | 2026 | planned | planned | Civil 3D |
| OpenBuildings Designer | 2024 | planned | planned | Revit |

Only **MicroStation 2026** is implemented so far. The other three are planned and intentionally not
yet scaffolded — the notes below capture the intended approach so they can be added consistently.

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

## Notes for the planned connectors

- **OpenRoads / OpenRail** — model these on the **Civil 3D** connector. They add the `Bentley.CifNET.*`
  civil SDK on top of MicroStation. Alignments, profiles and corridors should be surfaced as
  `DataObject`s carrying civil properties plus display curves (not legacy schema `Alignment` objects).
  The MicroStation converter is the geometry base; these projects reference it and add civil converters.
- **OpenBuildings** — primary workflow is **send/receive with Revit**, so align its data model and
  property extraction with the **Revit** connector's `DataObject` shape to keep round-trips clean. It adds
  the `Bentley.Building.Api` assemblies for grids and building elements.
