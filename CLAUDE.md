# CLAUDE.md

Guidance for Claude Code (and other AI assistants) working in this repository.

## What this repo is

`speckle-sharp-connectors` is the home of Speckle's next-generation .NET
projects for authoring-application integrations: the shared Desktop UI (DUI3),
host-application connectors, the converters that translate between host geometry
and the Speckle object model, and supporting SDK/tooling. Speckle is an AEC
(Architecture, Engineering, Construction) data hub, so the connectors plug into
CAD/BIM applications and publish or load model data.

The upstream repository is `github.com/specklesystems/speckle-sharp-connectors`.
The Speckle object model and core SDK live in a separate repo,
[`speckle-sharp-sdk`](https://github.com/specklesystems/speckle-sharp-sdk),
consumed here as NuGet packages (see `Local.slnx` for side-by-side work).

## Speckle integration guidance

- Follow the Speckle developer guidance for Building Integrations: choose the
  integration depth first (publish-only vs publish-and-load) and shape the
  implementation around that decision.
- Treat the connector as a data bridge. For send/publish, build a Speckle object
  graph using `Collection`, `Base`, `DataObject`, `displayValue`, `properties`,
  and instances/proxies.
- Preserve instances and proxies where possible (blocks, cells, families,
  shared definitions) instead of flattening everything.
- Keep host-specific semantics in `properties` while using `displayValue` for
  visible geometry.
- Support unsupported host elements with a fallback `DataObject` converter that
  preserves display geometry and metadata rather than silently dropping them.

## Repository layout

- `Connectors/` — host-application connectors, one folder per host
  (`Autocad`, `Bentley`, `CSi`, `Navisworks`, `Revit`, `Rhino`, `Tekla`, `TSD`).
  Each host typically has version-specific projects plus a `*Shared` shared-project
  that holds the bulk of the code.
- `Converters/` — geometry/data converters, one folder per host (same hosts as
  above, plus `Civil3d`, `Plant3d`). Same `*Shared` shared-project pattern.
- `DUI3/` — the shared WPF/WebView2 Desktop UI
  (`Speckle.Connectors.DUI`, `.DUI.WebView`, `.DUI.Tests`).
- `Sdk/` — shared building blocks:
  - `Speckle.Connectors.Common` — DI, operations, caching, threading, logging glue.
  - `Speckle.Converters.Common` — converter interfaces, registration, settings store.
  - `Speckle.Connectors.Logging` — OpenTelemetry.
  - `Speckle.Testing`, `*.Tests` — test infrastructure and unit tests.
- `Importers/` — file-import job processor and host-specific handler work.
- `Build/` — a C# Bullseye build project driving all repo automation.
- Root `*.slnx` files — one solution per host, plus `Speckle.Connectors.slnx`
  (everything) and `Local.slnx` (side-by-side with the SDK repo).

## Building and testing

- **.NET SDK 10.0.2xx** is required (pinned in `global.json`, `rollForward: latestMinor`).
- The repo uses a C# build runner. Invoke it through the wrapper scripts:
  - `./build.sh <target>` (Linux/macOS) or `.\build.ps1 <target>` (Windows).
  - Both forward to `dotnet run --project Build/Build.csproj -- <target>`.
- Common targets (from `Build/Program.cs`):
  - `build`, `restore`, `test`, `test-and-pack` (what CI runs), `pack`, `format`.
  - `clean-locks` — delete all `packages.lock.json` (needed when switching
    between `Speckle.Connectors.slnx` and `Local.slnx`).
  - `deep-clean` / `deep-clean-local` — delete all `bin`/`obj` and restore.
  - `generate-solutions`, `check-solutions`, `detect-affected`, `test-affected`.
- For normal development open `Speckle.Connectors.slnx`. Many host projects only
  build on Windows (they reference Windows-only host SDKs), so on Linux prefer
  building/testing the cross-platform SDK, converter, and DUI test projects.
- To iterate on SDK changes alongside this repo, use `Local.slnx` — but **never
  commit the `packages.lock.json` changes it produces**; revert with `clean-locks`.

### Package management

- Central Package Management is on (`ManagePackageVersionsCentrally`): declare
  versions in `Directory.Packages.props`, reference packages **without** a
  version in `.csproj` files.
- Lock files are enabled (`RestorePackagesWithLockFile`) — `packages.lock.json`
  is committed. Keep it in sync; don't hand-edit it.

## Coding conventions

Conventions are enforced by `.editorconfig`, `Directory.Build.props`, and
CSharpier — follow the existing code, and let the tools be the source of truth.

- **Language/runtime**: `LangVersion` 12, nullable enabled, implicit usings
  enabled. `unsafe` blocks are disallowed.
- **Warnings are errors** (`TreatWarningsAsErrors`) with
  `latest-AllEnabledByDefault` analyzers and enforced code style in build. A
  build with a new warning will fail — fix it, don't suppress it, unless it
  matches the existing `NoWarn` philosophy in `Directory.Build.props`.
- **Formatting is CSharpier** (`.csharpierrc.yaml`: 120 print width, 2-space
  indent). Run `dotnet csharpier format ./` (or `./build.sh format`) before
  committing; enable format-on-save if working in an IDE.
- **Naming** (from `.editorconfig`): PascalCase for non-private readonly fields,
  ALL_UPPER for constants, `s_` prefix + camelCase for private static fields,
  `_` prefix + camelCase for private instance fields. `this.` qualification is
  discouraged; explicit types are preferred over `var`.
- **Documentation file generation is on** (`GenerateDocumentationFile`), but
  missing XML-comment warnings (CS1591/CS1573) are suppressed.

### Architecture patterns

- **Dependency injection everywhere** (`Microsoft.Extensions.DependencyInjection`).
  Constructor injection is the norm; services register through `ServiceRegistration`
  / `ContainerRegistration` extension methods. Converters are auto-registered via
  `AddMatchingInterfacesAsTransient` and `AddApplicationConverters`.
- **Converters** implement small, composable interfaces from
  `Speckle.Converters.Common.Objects`:
  - `ITypedConverter<TIn, TOut>` — the workhorse: `TOut Convert(TIn target)`.
  - `IToSpeckleTopLevelConverter` / `IToHostTopLevelConverter` — top-level entry
    points that take/return `object`/`Base`, decorated with
    `[NameAndRankValue(typeof(HostType), rank)]` so the converter manager can
    dispatch by type and rank.
  - Compose smaller typed converters via constructor injection rather than one
    giant switch.
- **Type aliases via global usings**: converter projects define namespace
  aliases in `GlobalUsings.cs` — e.g. `SOG = Speckle.Objects.Geometry`,
  `RG = Rhino.Geometry`, `SA`, `SO`, `SOP`. Reuse the established aliases;
  don't fully-qualify these namespaces inline.
- **Per-host conversion settings** flow through
  `IConverterSettingsStore<TSettings>` (scoped), populated by a settings factory.

## Testing

- Test framework is **NUnit 4** with **Moq** for mocking and **AwesomeAssertions**
  (a FluentAssertions fork) for assertions. Shared helpers live in
  `Sdk/Speckle.Testing`.
- Test projects are the `*.Tests` folders (e.g. `Speckle.Converters.Common.Tests`,
  `Speckle.Connectors.Common.Tests`, `Speckle.Connectors.DUI.Tests`).
- New features and bug fixes should come with tests — the contributing guide
  says PRs without tests generally won't be merged.
- Run everything with `./build.sh test` (or `test-affected` for changed projects).

## Implementation workflow

- When changing the send/publish path, inspect the send binding, selection filter,
  root object builder, and instance unpacker in that order.
- When changing receive/load behavior, inspect the host object builder and the
  relevant host-side converters.
- When changing the UI/WebView bridge, keep initialization ordering safe and
  avoid executing scripts before the host is ready.
- When a change touches a connector for a host application, keep the implementation
  aligned with the Speckle “desktop host connectivity” and “data & build order”
  guidance rather than treating it as a host-only API wrapper.

## Git & PR workflow

- **Commit / PR titles** follow Conventional Commits scoped by project:
  `category(project): summary` — categories `feat`, `fix`, `docs`, `style`,
  `refactor`, `test`, `chore`. Example: `feat(revit): added category filter to send`.
- Keep commits focused; don't bundle unrelated refactors or doc churn.
- Cosmetic-only patches are generally not accepted (see `CONTRIBUTING.md`) —
  a change should improve stability or functionality.
- The PR template (`.github/pull_request_template.md`) asks for Description,
  User Value, Changes, Validation, and a checklist (related commits, tests,
  docs). Fill it in.
- CI (`.github/workflows/pr.yml`) runs `./build.sh test-and-pack` and lints the
  GitHub Actions with zizmor. Make sure the build is warning-clean and formatted
  before pushing.
- `CODEOWNERS` routes reviews per host area; expect the owning team to review.

## Gotchas

- Don't commit `packages.lock.json` changes caused by `Local.slnx`.
- Warnings fail the build — treat analyzer output as blocking.
- Many connector/converter projects target Windows-only host SDKs and won't
  compile on Linux; scope local builds to what your platform supports.
- Some paths are excluded from Copilot context via `copilot.ignore`
  (`Speckle.Performance/`, `Connectors/`).
