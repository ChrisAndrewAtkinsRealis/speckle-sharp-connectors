# Copilot instructions — speckle-sharp-connectors

This repository implements Speckle connectivity for authoring applications: shared desktop UI, host-application connectors, and the converters that translate between host geometry and the Speckle object model. Follow the Speckle developer guidance for Building Integrations, Publish & Load, and Data & Build Order: decide early whether the change is publish-only or publish-and-load, then design the object graph and converter strategy around that choice.

## Speckle integration principles

- Treat connector work as integration architecture, not just a one-off host API wrapper.
- Start with the send/publish path and only add receive/load logic when the integration depth requires it.
- Build Speckle data using the documented primitives: `Collection`, `Base`, `DataObject`, `displayValue`, `properties`, and instance/proxies. Favor `displayValue` for visible geometry and keep host-specific semantics in `properties`.
- Preserve instances and proxies where possible (blocks, cells, families, shared definitions) instead of flattening them.
- Use stable application ids and host-native metadata so the connector can round-trip and be re-identified reliably.

## Repository layout

- `Connectors/<Host>/` — host connectors and host-specific services, with version-specific projects plus a `*Shared` project holding most shared logic.
- `Converters/<Host>/` — geometry/data converters, same `*Shared` pattern.
- `DUI3/` — shared WPF/WebView2 desktop UI and bridge components.
- `Sdk/` — shared infrastructure: `Speckle.Connectors.Common` (DI, operations, caching), `Speckle.Converters.Common` (converter interfaces/registration), `Speckle.Connectors.Logging`, `Speckle.Testing`, and `*.Tests`.
- `Importers/` — file-import job processor.
- `Build/` — C# Bullseye build runner.

## Architecture

- Use dependency injection everywhere; prefer constructor injection and service registration in `ServiceRegistration` / `ContainerRegistration` extensions.
- Keep converters small and composable. Top-level send/receive converters implement `IToSpeckleTopLevelConverter` / `IToHostTopLevelConverter` and are registered with `[NameAndRankValue(typeof(HostType), rank)]`.
- Reuse typed/raw converters for geometry primitives rather than building large switch statements.
- Support unsupported host elements with a fallback converter that emits a `DataObject` with `displayValue` and metadata instead of silently dropping the element.
- Use `IConverterSettingsStore<TSettings>` for per-host conversion settings and `MicroStationConversionSettings`-style settings objects for host-specific behavior.
- For cells/blocks/instances, prefer instance proxies and definitions over converting each placement as a standalone geometry object.

## Build & test

- Requires **.NET SDK 10.0.2xx** (pinned in `global.json`).
- Run the build runner through wrappers: `./build.sh <target>` / `./build.ps1 <target>` or `./build.ps1 <target>`. Key targets: `build`, `test`, `test-and-pack` (CI target), `format`, `clean-locks`, `deep-clean`.
- Open `Speckle.Connectors.slnx` for normal work; `Local.slnx` is for side-by-side SDK development — **never commit `packages.lock.json` changes it produces**.
- Many host projects are Windows-only; scope local builds accordingly.

## Conventions

- C# `LangVersion` 12, nullable + implicit usings enabled, no `unsafe`.
- **Warnings are errors** with all analyzers enabled and code style enforced in build — fix warnings, don't suppress them.
- **Format with CSharpier** (120 print width, 2-space indent): run `dotnet csharpier format ./` before committing.
- Naming (`.editorconfig`): PascalCase non-private readonly fields, ALL_UPPER constants, `s_`+camelCase private static fields, `_`+camelCase private instance fields; prefer explicit types over `var`; avoid `this.`.

## Packages & tests

- Central Package Management: declare versions in `Directory.Packages.props`, reference packages **without** versions in `.csproj`. Lock files are committed.
- Tests use **NUnit 4** + **Moq** + **AwesomeAssertions**; shared helpers live in `Sdk/Speckle.Testing`. New features and bug fixes should include tests.

## Implementation guidance for this repo

- If a task touches the send pipeline, inspect the send binding, selection filter, root object builder, and instance unpacker in that order.
- If a task involves unsupported host objects, add or improve fallback conversion before assuming the object should be dropped.
- If a task changes cell/shared-block behavior, preserve instance definitions and placement transforms rather than flattening them.
- If a task touches the UI bridge or WebView layer, keep initialization ordering safe and avoid executing scripts before the host is ready.

## Git & PRs

- Commit / PR titles use Conventional Commits scoped by project: `category(project): summary` (`feat`, `fix`, `docs`, `style`, `refactor`, `test`, `chore`), e.g. `feat(revit): added category filter to send`.
- Keep commits focused; cosmetic-only patches are generally not accepted.
- Fill in the PR template; CI runs `./build.sh test-and-pack` and must be warning-clean and CSharpier-formatted.
