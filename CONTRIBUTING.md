# Contributing to Radpa

Thank you for helping improve **Radpa (ردپا)**!

## Architecture & Ground Rules
1. Keep strict dependency direction: `Radpa.Domain` <- `Radpa.Application` <- `Radpa.Infrastructure` <- `Radpa.UI`. Never introduce circular references or put EF Core / UI types into `Domain` or `Application`.
2. Preserve AOT & trimming compatibility in `Radpa.Domain`, `Radpa.Application`, `Radpa.Infrastructure`, and `Radpa.UI`:
   - Use compiled bindings (`x:CompileBindings="True"`) in every `.axaml` file.
   - Use `System.Text.Json` source generation (`JsonSerializerContext`), not reflection serialization.
   - Use `[LibraryImport]` source-generated P/Invoke for Win32 interop.
3. Preserve privacy-by-default: window titles and elevated network capture stay **off** by default, and no telemetry or background external network requests (other than the configurable GitHub Release check) may be added.
4. Keep `.editorconfig` and analyzer compliance cleanly passing (`TreatWarningsAsErrors=true`, `Nullable=enable`, `var` for local variables).

## Local Development Workflow
1. Install **.NET 10 SDK** matching `global.json`.
2. Restore and test:
   ```powershell
   dotnet restore Radpa.sln
   dotnet format Radpa.sln --verify-no-changes
   dotnet build Radpa.sln -c Release
   dotnet test Radpa.sln -c Release
   ```
3. To run the application locally during development:
   ```powershell
   dotnet run --project src/Radpa.UI/Radpa.UI.csproj -- --single-process
   ```
4. Check `docs/DECISIONS.md` when making architectural changes and update documentation/tests in the same pull request.
