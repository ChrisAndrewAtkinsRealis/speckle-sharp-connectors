# Copilot instructions — speckle-sharp-connectors

Next-generation Speckle .NET projects: the shared Desktop UI (DUI3),
host-application connectors, and the converters that translate between host
geometry and the Speckle object model. Speckle is an AEC data hub; connectors
plug into CAD/BIM applications (AutoCAD, Civil3D, Revit, Rhino, Tekla, CSi,
Navisworks). The Speckle object model / core SDK lives in the separate
`speckle-sharp-sdk` repo and is consumed here as NuGet packages.

## Layout

- `Connectors/<Host>/` — connectors, with version-specific projects plus a
  `*Shared` shared-project holding most code.
- `Converters/<Host>/` — geometry/data converters, same `*Shared` pattern.
- `DUI3/` — shared WPF/WebView2 desktop UI.
- `Sdk/` — shared building blocks: `Speckle.Connectors.Common` (DI, operations,
  caching), `Speckle.Converters.Common` (converter interfaces/registration),
  `Speckle.Connectors.Logging` (OpenTelemetry), `Speckle.Testing`, and `*.Tests`.
- `Importers/` — file-import job processor. `Build/` — C# Bullseye build runner.

## Build & test

- Requires **.NET SDK 10.0.2xx** (pinned in `global.json`).
- Run the build runner through wrappers: `./build.sh <target>` /
  `.\build.ps1 <target>`. Key targets: `build`, `test`, `test-and-pack`
  (CI target), `format`, `clean-locks`, `deep-clean`.
- Open `Speckle.Connectors.slnx` for normal work; `Local.slnx` for side-by-side
  SDK development — **never commit the `packages.lock.json` changes it produces**.
- Many host projects are Windows-only; scope local builds accordingly.

## Conventions

- C# `LangVersion` 12, nullable + implicit usings enabled, no `unsafe`.
- **Warnings are errors** with all analyzers enabled and code style enforced in
  build — fix warnings, don't suppress them.
- **Format with CSharpier** (120 print width, 2-space indent): run
  `dotnet csharpier format ./` before committing.
- Naming (`.editorconfig`): PascalCase non-private readonly fields, ALL_UPPER
  constants, `s_`+camelCase private static fields, `_`+camelCase private
  instance fields; prefer explicit types over `var`; avoid `this.`.

## Architecture

- **Dependency injection everywhere** (`Microsoft.Extensions.DependencyInjection`),
  constructor injection; services register via `ServiceRegistration` /
  `ContainerRegistration` extension methods. Converters auto-register through
  `AddMatchingInterfacesAsTransient` / `AddApplicationConverters`.
- **Converters** implement small composable interfaces from
  `Speckle.Converters.Common.Objects`: `ITypedConverter<TIn, TOut>` is the
  workhorse; `IToSpeckleTopLevelConverter` / `IToHostTopLevelConverter` are the
  top-level entry points, decorated with `[NameAndRankValue(typeof(HostType), rank)]`.
  Compose small typed converters via constructor injection rather than one big switch.
- **Namespace aliases** come from each project's `GlobalUsings.cs`
  (`SOG = Speckle.Objects.Geometry`, `RG = Rhino.Geometry`, `SA`, `SO`, `SOP`, …).
  Reuse the established aliases instead of fully-qualifying inline.
- Per-host conversion settings flow through `IConverterSettingsStore<TSettings>`.

## Packages & tests

- Central Package Management: declare versions in `Directory.Packages.props`,
  reference packages **without** versions in `.csproj`. Lock files are committed.
- Tests use **NUnit 4** + **Moq** + **AwesomeAssertions**; shared helpers in
  `Sdk/Speckle.Testing`. New features and bug fixes should include tests.

## Git & PRs

- Commit / PR titles use Conventional Commits scoped by project:
  `category(project): summary` (`feat`, `fix`, `docs`, `style`, `refactor`,
  `test`, `chore`), e.g. `feat(revit): added category filter to send`.
- Keep commits focused; cosmetic-only patches are generally not accepted.
- Fill in the PR template; CI runs `./build.sh test-and-pack` and must be
  warning-clean and CSharpier-formatted.
