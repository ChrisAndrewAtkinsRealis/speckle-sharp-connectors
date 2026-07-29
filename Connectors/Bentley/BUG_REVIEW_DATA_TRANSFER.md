# MicroStation connector/converter review — bugs affecting round-trip data transfer

Scope: `Connectors/Bentley/Speckle.Connectors.MicroStationShared`,
`Converters/Bentley/Speckle.Converters.MicroStationShared`,
`Converters/Bentley/Speckle.Converters.OpenRoadsShared`, plus the shared SDK
plumbing they rely on (`Sdk/Speckle.Converters.Common`,
`Sdk/Speckle.Connectors.Common`). Goal: find bugs that stop or corrupt data
transfer host → Speckle → host, break display fidelity, or drop data/object
types. Findings are code-verified on this branch; none require a live
MicroStation session to see in the source, but fixes touching the Bentley API
surface must be validated against a live SDK build (these projects do not
compile on Linux).

Severity legend: **[BLOCKER]** wrong/stale/lost data in normal use,
**[HIGH]** wrong display or lost data for common cases, **[MED]** wrong
data for specific object types/workflows, **[LOW]** quality/latent.

---

## A. Send pipeline (host → Speckle)

### A1. [BLOCKER] Send conversion cache is never evicted — edits never reach Speckle after the first send
`MicroStationRootObjectBuilder.ConvertOrProxy` reads
`ISendConversionCache` (`MicroStationRootObjectBuilder.cs:188`), and the cache
is registered as a **singleton** (`ServiceRegistration.cs`, connector). Every
other connector evicts on change (`RhinoSendBinding.cs:364`,
`AutocadSendBaseBinding.cs:162`, `TeklaSendBinding.cs:152`, Revit change
tracker); the Bentley connector never calls `EvictObjects` anywhere, and has
no element/document change tracking at all. MicroStation element ids are
stable per file, so after the first successful send of a project, every later
send of the same element returns the stale cached `ObjectReference`: moved,
edited, recolored geometry silently re-sends its **old** state until the
application restarts. Model cards are also never marked expired.

Fix direction: hook MicroStation change notifications (element changed/undo,
or at minimum evict the ids of the current selection on every send / on
document swap in `MicroStationDocumentModelStore.OnDocumentSwap`).

### A2. [BLOCKER] Per-send reference-origin recentering is incompatible with the cross-send cache
`ReferencePointConverter` makes “whichever point converts first” the origin of
that send, and all geometry is stored relative to it; the origin is recorded
on the root (`MicroStationRootObjectBuilder.cs:150-154`). But cached
`ObjectReference`s (A1) were recentered against the **previous** send’s
origin. On a second send with a different selection (different first point),
fresh conversions use origin O2 while cached objects still embed O1, and the
root advertises only O2. Result: objects in the same version are mutually
offset by O1−O2 in the viewer and on receive. Even with A1 fixed (eviction of
changed ids only), any partial cache hit re-introduces this.

Fix direction: make the origin deterministic and stable per model (e.g. the
model’s global origin, or persist the first-ever origin per project/model),
or evict the whole cache whenever the computed origin differs from the one
the cached objects were built with.

### A3. [BLOCKER] Range-fallback mesh is built in UoRs, not master units — wrong scale and it can poison the whole send
`ElementToSpeckleFallbackConverter.TryAddRangeFallbackMesh` reads element
range bounds via reflection (native ranges are in **UoRs**) and feeds the
corner points straight into
`referencePointConverter.ConvertToExternalCoordinates(new BG.DPoint3d(x, y, z))`
without dividing by `UorPerMaster` — every other converter divides first
(compare `DPoint3dToSpeckleRawConverter`). Consequences:
1. The fallback box is `UorPerMaster`× too large (typically 10 000×) and in
   the wrong place.
2. Far worse: if that mesh happens to be the **first** geometry converted in
   the send, the shared reference origin is seeded with a UoR-scale value,
   and *every subsequent object in the entire send* is offset by it.

Fix: divide the range corners by `settingsStore.Current.UorPerMaster` before
recentering (same pattern as `PolyfaceHeaderToSpeckleRawConverter`).

### A4. [BLOCKER] EC properties and Item Types are never attached on send
The enrichment that adds `PropertiesExtractor` output and
`properties["Item Types"]` lives in
`MicroStationRootToSpeckleConverter.Convert` — but nothing in the MicroStation
send path uses `IRootToSpeckleConverter`. `MicroStationRootObjectBuilder`
resolves top-level converters directly
(`MicroStationRootObjectBuilder.cs:200-202`) and attaches nothing. Compare
Rhino/Tekla/CSi/Revit builders, which all go through their root converter.
Net effect: for every non-cell element, all EC data and Item Type data is
dropped — “all data must be transferred” is broken, and the receive-side
`MicroStationItemTypeBaker` (which looks for `properties["Item Types"]`) has
nothing to read. Only cell instance proxies get properties (attached in
`MicroStationInstanceUnpacker.AddInstanceProxy`).

Fix direction: either route `ConvertOrProxy` through
`IRootToSpeckleConverter`, or apply the same property/Item-Type enrichment in
the builder after `objectConverter.Convert(element)`. Note
`MicroStationRootToSpeckleConverter` also has its own defect: it calls
`GetProperties(element)` twice and merges the result into itself, and it
writes a bootstrap log file on every single conversion (severe I/O overhead on
large sends) — clean that up when wiring it in.

### A5. [HIGH] Instance (cell) transforms bypass the reference-origin recentering — cells display offset in Speckle
All atomic geometry, including shared-cell *definition* geometry, is
recentered by −origin, but `MicroStationInstanceUnpacker.AddInstanceProxy`
stores the placement transform with its **absolute** translation
(`MicroStationTransformHelper.ToInstanceMatrix`). A consumer composes
`instance = T · defGeometry`, so a placement with rotation R lands at
`R·(L−origin)+T = world − R·origin`, while all non-instance geometry sits at
`world − origin`. Any rotated placement is therefore displaced by
`(I−R)·origin` — at civil-scale origins (10⁵–10⁶ master units) that is
kilometres. MicroStation→MicroStation round-trip self-cancels (the receive
path un-recenters the same geometry), which hides the bug; every other
consumer (viewer, Rhino, Revit) shows it.

Fix direction: recentre the *transform translation* (`tr/uor − origin`) and
do **not** recentre definition-space geometry (definition children should be
converted with recentering disabled), so definitions stay local and
placements carry the offset exactly once.

### A6. [HIGH] Normal (non-shared) cells: placement transform applied to already-placed children — likely double transform
`MicroStationInstanceUnpacker.UnpackCell` builds a one-off definition from
`EnumerateChildren(cell)` — a `CellHeaderElement`’s children are stored
already placed (world coordinates) — and simultaneously emits an
`InstanceProxy` whose transform is whatever
`TransformCaptureProcessor.AnnounceTransform` reports. If a non-identity
transform is announced for the header, consumers apply it on top of
world-placed children → double transform. This is the same class of bug the
earlier shared-cell fix addressed (see
`plan-fix-multi-object-transfer-in-microstation-connector.md`), which
explicitly relied on the assumption that headers announce identity — that
assumption is unverified. Verify on a live build; if a transform is announced,
either bake the children through the inverse transform or emit the proxy with
identity.

### A7. [MED] Cells selected inside reference attachments break instancing and duplicate geometry
For a reference element the atomic `applicationId` is `R{attachmentId}:{elementId}`
(`MicroStationReferenceService.EncodeReferenceId`), but
`UnpackSharedCell`/`UnpackCell` key the `InstanceProxy` by plain
`instance.ElementId.ToString()`. In `ConvertOrProxy` the lookup
`instanceProxies.TryGetValue(applicationId, …)` then never matches, so the
placement is converted as flattened geometry via the fallback **and** the
orphaned proxy/definition (with its children added as extra atomic objects)
is still emitted — duplicated geometry and broken instances whenever
“include references” is on and the reference contains cells.

Fix: pass the element’s composite application id into the unpacker instead of
re-deriving it from `ElementId` (`UnpackSelection` already has `obj.ApplicationId`).

### A8. [MED] Reference attachment transform and units are ignored
Reference elements are converted with the active model’s
`UorPerMaster`/units and no attachment transform (acknowledged as a follow-up
in `MicroStationReferenceService` remarks). Any attachment that is moved,
rotated, scaled, or authored in different master units sends misplaced/
mis-scaled geometry. Until per-reference settings exist, consider excluding
non-identity-transform attachments with a clear per-object error instead of
sending wrong geometry silently.

### A9. [MED] Closed B-spline curves: duplicated pole without fixing weights/knots
`MSBsplineCurveToSpeckleRawConverter.Convert`: for closed curves it appends
`poles[0]` to the pole list, but `weights` is taken from `target.Weights`
un-lengthened (mismatch of `points.Count/3 == n+1` vs `weights.Count == n`
for rational curves) and `knots` is left untouched. Consumers that rebuild
the NURBS from counts (`points`, `weights`, `knots`, `degree`) get an
inconsistent definition; rational closed curves will fail to rebuild or
deform. Fix: when duplicating the pole, duplicate the weight too (and
document the knot convention — see A10/B3).

### A10. [MED] Knot-vector convention never normalized (send side)
Bentley knot vectors are full-length (`poles + order`); Rhino-style Speckle
curves carry `poles + degree − 1`. The send path exports Bentley’s knots
verbatim. Several receivers tolerate both, but the Speckle object model
convention is the Rhino-style vector; exporting the full vector risks
misinterpretation in strict consumers. Normalize (drop first/last knot) on
send, mirroring what other v3 converters do.

### A11. [MED] Element colours: true-colour and ByLevel/ByCell elements lose their colour
`MicroStationColorUnpacker.TryGetArgb` treats the raw colour uint as an index
into `DgnColorMap.GetTbgrColors()`. That is only valid for table indices
0–255. True-colour values (packed TBGR with the extended flag),
`ByLevel` (0xFFFFFFFF), and `ByCell` all index out of range, throw, and are
swallowed → those elements ship with no colour proxy at all, losing display
fidelity in Speckle. Handle: detect true-colour and unpack directly; resolve
ByLevel through the element’s level; resolve ByCell through the parent cell.

### A12. [MED] Surface control-net ordering likely transposed
`ElementToSpeckleDataObjectBuilder.ConvertSurface` indexes poles as
`u * vCount + v`. Bentley `MSBsplineSurface` stores poles U-fastest
(`index = v * uPoleCount + u`). For any surface with `UPoleCount ≠ VPoleCount`
this reads the wrong poles entirely (out-of-order net → garbled surface);
even for square counts the net is transposed relative to
`degreeU/degreeV/knotsU/knotsV`. Verify against a live SDK and swap the
indexing if confirmed. Knot vectors here have the same convention issue as
A10.

### A13. [LOW] OpenRoads: `endStation` mixes UoR length with station value
`AlignmentToSpeckleConverter`: `properties["endStation"] =
target.LinearGeometry.Length + stationing.StartStation`. CifNET linear
geometry lengths are in UoRs while stations are master-unit values — the sum
is dimensionally wrong (off by `UorPerMaster`). Divide the length by
`settingsStore.Current.UorPerMaster` (verify against live SDK; some CifNET
members return master units).

### A14. [LOW] Civil entities are sent on every send regardless of the user’s selection filter
`CivilModelContributor.Contribute` enumerates *all* alignments/corridors of
the active connection and appends them to every send. A user sending two
lines gets the whole civil model attached. Consider gating on selection or a
send setting.

---

## B. Receive pipeline (Speckle → host)

### B1. [HIGH] Received ellipses lose their plane — baked axis-aligned at the world origin orientation
`EllipseToHostConverter` builds `new BG.DPlacementZX(origin)` from the plane
**origin only**; `target.plane.normal/xdir/ydir` are ignored, as is any
in-plane rotation. Every ellipse not lying in the default plane orientation
is baked with wrong orientation (only its centre survives). Build the
placement from the plane’s axes (rotation matrix from xdir/ydir/normal), as
the Arc/Circle converters effectively do via their geometric constructions.

### B2. [HIGH] Item Types can never be written back
`MicroStationItemTypeBaker.ApplySingleItemType` uses
`host.GetCustomItem(libraryName, itemTypeName)`, which returns an Item Type
instance **already attached to that element**. Freshly created elements have
none, so this always returns null and the baker logs “not defined in the
target file” for every element — Item Type round-trip is a permanent no-op
(compounded by A4: the data isn’t even sent). To attach to a new element the
flow is: locate the `ItemTypeLibrary`/`ItemType` definition in the file, then
`host.ApplyCustomItem(itemType)`, then set values. Two more defects in the
same path:
- The extractor keys entries by `ClassDefinition.DisplayLabel` (falls back to
  `Name`), but the lookup needs the EC class **Name**; display labels with
  spaces/localization will never match. Store the class name (keep the label
  as extra metadata).
- `ApplyItemTypes` runs **before** `element.AddToModel()`
  (`MicroStationHostObjectBuilder.AddToModel`); EC attachment to
  not-yet-persisted elements is at best unreliable — apply after the element
  is in the model.

### B3. [HIGH] Receiving freeform curves from other hosts: knot count mismatch
`CurveToHostConverter` passes `target.knots` straight into
`MSBsplineCurve.CreateFromPoles(points, weights, knots, order, closed, false)`.
Bentley expects a full knot vector (`poles + order`); curves from Rhino/Revit
carry `poles + degree − 1`. The call will fail or produce a wrong curve for
every received NURBS authored elsewhere. Normalize by padding the first/last
knot when the count is the Rhino-style one. Also: after
`CreateFromPoles(..., closed: true, ...)` the code calls `MakeClosed()` again
— verify this doesn’t re-wrap an already-closed curve.

### B4. [MED] Received cell constituents are left in the model as loose duplicates
`MicroStationHostObjectBuilder` bakes definition constituents with
`AddToModel()` (step 2), then `MicroStationInstanceBaker.CreateDefinition`
adds those same persisted elements as children of a
`SharedCellDefinitionElement` and adds the definition to the model. The
originals are only removed from the *result bookkeeping*
(`bakedObjectIds.RemoveWhere`), never from the DGN model — unless
`AddChildElement` re-parents (verify on live SDK), each cell’s geometry
exists twice: once loose at its baked location and once per placement.
If re-parenting doesn’t happen implicitly, delete the loose originals after
the definition is created.

### B5. [MED] Re-receive appends instead of replacing
`MicroStationInstanceBaker.PurgeInstances` is an acknowledged no-op, and the
host object builder has no pre-receive cleanup of previously baked elements
(remarked as MVP). Receiving the same version twice duplicates the whole
model. This is a known limitation, but it directly violates “data must
display properly in host” for the everyday re-receive workflow, so it belongs
on the fix list rather than the backlog.

### B6. [LOW] `DataObjectConverter` is dead code — only the highest-rank converter per type is registered
`Sdk/.../ServiceRegistration.AddConverters` keeps only the top-ranked
converter per target type (`ServiceRegistration.cs:62-69`).
`SolidLikeDataObjectToHostConverter` (rank DEFAULT+1) therefore shadows
`DataObjectConverter` (rank DEFAULT) completely; there is no rank cascade at
dispatch time. Non-solid DataObjects survive only because
`SolidLikeDataObjectToHostConverter` throws `ConversionNotSupportedException`
and `ConverterWithFallback` falls back to display values — which works, but
means the purpose-built DataObject path never runs and metadata-only
DataObjects (empty displayValue) fail with a generic error. Either merge the
solid-like special case into `DataObjectConverter` as one converter, or drop
the dead one.

### B7. [LOW] Received level names collide with the root collection
`MicroStationHostObjectBuilder.GetLevelName` walks up to the nearest named
`Collection`. Objects sitting directly under the root (e.g. the `Civil`
collection contents, or root-level objects from other connectors) get a level
named after the root/model or “Civil” — harmless but surprising; consider
filtering the root collection out.

---

## C. Smaller correctness notes (worth fixing while in the area)

1. `MeshToHostConverter`: `if (n < 3) n += 3` maps the legacy face codes 0/1
   correctly but silently turns an (invalid) `2` into a pentagon — guard and
   throw instead.
2. `DEllipse3dToSpeckleRawConverter`: full-circle detection compares
   start/end points with `1e-9` in **UoRs** — practically always true for
   tiny arcs of large radius? No: 1e-9 UoR is subatomic; fine. The sweep
   check `|sweep|−2π < 1e-9` is the real detector; OK. No action.
3. `MicroStationRootToSpeckleConverter`: double property extraction and
   unconditional `File.AppendAllText` logging per element (see A4).
4. `ElementToSpeckleDataObjectBuilder.Convert` adds curve display values
   *and* facet meshes for the same element when both exist — duplicate
   display geometry (curve outline + mesh) for filled shapes; decide on one.
5. `MicroStationColorUnpacker` colours are keyed for instance placements too,
   but definition children of shared cells inherit ByCell colour — related to
   A11.
6. `CivilHostRebuilder.CanRebuild` matches any `DataObject` whose `["type"]`
   is `"Alignment"`/`"Corridor"` — a non-civil object named that way from
   another connector would be misrouted (low likelihood; tighten by checking
   for the schema properties too).

---

## Suggested fix order

1. A1 + A2 (cache eviction / origin stability) — without these, *nothing*
   sent after the first send can be trusted.
2. A3 (UoR range mesh) — one-line unit fix, removes a whole-send corruption
   trigger.
3. A4 + B2 (properties/Item Types send + receive) — restores the “all data”
   guarantee.
4. B1, B3, A9/A10 (ellipse plane, knot normalization) — restores geometry
   fidelity for curves/ellipses.
5. A5/A6/A7, B4/B5 (instances) — cells correct in Speckle and on
   receive.
6. A11, A12, A13, remainder.

All Bentley-API-touching fixes (A3, A5–A8, A11, A12, B1–B5) need validation
against a live MicroStation 2026 build per the repo’s existing practice —
these projects are Windows-only.
