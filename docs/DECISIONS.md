# Architectural & Engineering Decisions (`docs/DECISIONS.md`)

This document records every product, Phase 0 Risk Gate, library, and security decision along with rationale and alternatives considered.

---

## ADR-001: Product Name — **Radpa (ردپا)**
- **Decision**: Named the application **Radpa** (`ردپا` in Persian, meaning *footprint* / *trail*).
- **Rationale**: Short, memorable across both Persian and English, directly reflects Digital/Resource Footprint transparency without implying invasive spying or cloud tracking.

---

## ADR-002: Gate A — Packaging & Publish Strategy (Fallback A)
- **Requirement**: Single self-contained executable (`win-x64`, `win-arm64`), smallest footprint, no .NET runtime preinstalled on user machines.
- **Observed Fact (Phase 0 Investigation)**: Local toolchain audit showed `.NET SDK 10.0.114` installed alongside VS 2022 Enterprise (`17.11.4`) **without** MSVC C++ Build Tools or Windows 10/11 SDK, and no `clang`/`lld-link`. Native AOT (`PublishAot=true`) on Windows requires the native MSVC linker (`link.exe`) and Windows SDK libraries (`kernel32.lib`, `user32.lib`, etc.), so AOT cannot link on machines lacking C++ Build Tools.
- **Decision (Fallback A)**:
  - Keep `IsTrimmable=true` and `IsAotCompatible=true` enforced on `Radpa.Domain`, `Radpa.Application`, `Radpa.Infrastructure`, and `Radpa.UI` (preventing accidental reflection bloat, enforcing `[LibraryImport]` and `JsonSerializerContext`, and retaining future AOT portability).
  - Publish `Radpa.UI` on RID publish (`win-x64`, `win-arm64`) with `PublishSingleFile=true`, `SelfContained=true`, `PublishReadyToRun=true`, `PublishTrimmed=true`, `TrimMode=partial`, `InvariantGlobalization=true`, `EnableCompressionInSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, and `StripSymbols=true`.
  - Keep a split `--collector` (pure Win32 + .NET core libraries, zero Avalonia/Skia loaded) and `--ui` process model so background memory stays tiny without AOT-linking friction on contributor machines.

---

## ADR-003: Gate B — ETW Per-Process Network Collector Isolation (Fallback B)
- **Requirement**: Capture per-process TCP & UDP (IPv4 & IPv6) sent and received bytes via ETW (`Microsoft-Windows-Kernel-Network`), never request UAC/Admin on startup, and keep `Radpa.exe` lightweight and non-elevated.
- **Observed Fact**:
  - Windows kernel network ETW sessions require Administrator (`SeSystemProfilePrivilege` / Admin group).
  - `Microsoft.Diagnostics.Tracing.TraceEvent 4.0.0` uses reflection and TDH parsing metadata that defeats trimming/AOT and inflates the main UI binary if bundled inside `Radpa.exe`.
- **Decision (Fallback B)**:
  - Isolate `TraceEvent` cleanly inside `src/Radpa.NetCollector` (`Radpa.NetCollector.exe`, `<IsTrimmable>false</IsTrimmable>`).
  - Detailed network tracking is **OFF by default**. When toggled on in Settings, `WindowsElevationService` prompts via UAC (`Verb = "runas"`) and starts `Radpa.NetCollector.exe` with `--pipe <random>`, `--sid <current-user-SID>`, and `--parent <pid>`.
  - `Radpa.NetCollector` creates a `NamedPipeServerStreamAcl` permitting ONLY `CurrentUserSID` and `BuiltinAdministratorsSid`, monitors `--parent` exit to shut down cleanly if orphaned, and exits cleanly if the user cancels UAC (`Win32Exception` code `1223`) without affecting the rest of Radpa.

---

## ADR-004: Gate C — Persian RTL Layout, Vazirmatn UI Font Subset & Compiled DataGrid Bindings
- **Observed Fact**:
  - `Avalonia` 12.1.6 `DataGridTextColumn` header properties reside outside the visual tree inheritance chain, making `{Binding Loc.ColApp}` fail under `x:CompileBindings="True"`.
  - Font subsetting full `Vazirmatn-UI` TTFs (`~280 KB+` each) with Python `fonttools pyftsubset` (`--layout-features=* --name-IDs=* --unicodes=...`) produced clean Persian/Arabic + Latin/digit fonts:
    - `Vazirmatn-UI-Regular.ttf`: `82,320` bytes
    - `Vazirmatn-UI-Medium.ttf`: `82,260` bytes
    - `Vazirmatn-UI-Bold.ttf`: `82,640` bytes
    - Bundled `OFL.txt`: `4,391` bytes
  - Vector multi-size `radpa.ico`: `4,077` bytes.
- **Decision**:
  - Keep `x:CompileBindings="True"` across 100% of `.axaml` views (`<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>`).
  - Update `DataGrid` column headers explicitly from `Localizer.PropertyChanged` in `DashboardView.axaml.cs` and `PrivacyAuditorView.axaml.cs`.
  - Bind column `SortMemberPath` to `Raw.<NumericOrTimeSpanProperty>` so sorting always compares true numeric values even when display strings use Persian digits (`۰-۹`) and RTL unit isolation (`\u200E`).
  - Render charts (`HorizontalBarChartControl`, `TimelineChartControl`) directly on Avalonia `DrawingContext` using `Vazirmatn UI` and native `FlowDirection` flipping—eliminating heavy reflection-based charting dependencies.

---

## ADR-005: Plain ADO.NET SQLite vs SQLCipher / EF Core
- **Decision**: Use `Microsoft.Data.Sqlite 10.0.15` with raw parameterized SQL in WAL mode (`PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON;`), avoiding EF Core entirely. Optional privacy protection uses native .NET `AesGcm` (`AES-256-GCM`, `"ENC1:"` envelope) with keys wrapped via Windows DPAPI (`ProtectedData`, `DataProtectionScope.CurrentUser`), rather than bundling a third-party native SQLCipher binary.
- **Rationale**: Faster cold startup, zero ORM query-plan allocations, minimal single-file footprint, and zero risk of AOT/linker native library mismatches.

---

## ADR-006: Manual Composition Root & Zero-Reflection Localization
- **Decision**: Construct dependencies explicitly in `src/Radpa.UI/CompositionRoot.cs` rather than `IServiceProvider`, and store Persian/English UI strings in statically typed properties on `Localizer` (`ObservableObject`).
- **Rationale**: Zero startup reflection, trivial compile-time refactoring safety, and full compatibility with partial/full IL trimming and Native AOT.

---

## ADR-007: Test Library Licensing & Framework Versions
- **Decision**: Pinned `FluentAssertions` at `7.2.0` (Apache-2.0 for all users) rather than `8.x` (Xceed commercial license for non-OSS/enterprise contexts). Pinned `xunit.v3 3.2.2`, `Avalonia.Headless.XUnit 12.1.6`, `NSubstitute 6.2.0`, `BenchmarkDotNet 0.15.8`, and `Roslynator.Analyzers 5.0.1` alongside `Microsoft.CodeAnalysis.NetAnalyzers 10.0.404`.

---

## ADR-008: Verification Status on Authoring Machine vs CI
- **Observed Condition**: Local package restore on the authoring machine was intentionally deferred to GitHub Actions CI (`nuget.config` points strictly to `https://api.nuget.org/v3/index.json`).
- **Verified Locally**:
  - `.NET SDK 10.0.114` environment detection, MSVC/SDK absence audit, `Vazirmatn-UI` font subsetting & OFL extraction, multi-resolution `radpa.ico` generation, `Radpa.sln` creation (`dotnet new sln` / `dotnet sln add`), `Radpa.Domain` and `Radpa.Application` standalone builds (`0 Warning(s), 0 Error(s)` prior to final test/csproj refinements), and GitHub Actions workflow validation via `actionlint v1.7.12`.
- **Executed in GitHub Actions CI & Release (`windows-latest`)**:
  - `dotnet restore Radpa.sln`
  - `dotnet build Radpa.sln -c Release --no-restore -p:CI=true`
  - `dotnet format Radpa.sln --verify-no-changes --no-restore`
  - `dotnet test` (Domain, Application with >= 85% line coverage gates; Infrastructure + WindowsLive; Avalonia.Headless UI/RTL tests; Probe C RTL headless test)
  - `dotnet publish` for `win-x64` and `win-arm64` + `Radpa.exe --smoke`
