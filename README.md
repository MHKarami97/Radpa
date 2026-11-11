# Radpa (ردپا) — Privacy-First Windows App, Resource & Network Monitor

> [نسخه فارسی README](README.fa.md)

**Radpa (ردپا)** is a lightweight, local-only Windows 10/11 desktop monitor written in **.NET 10** and **Avalonia**. It shows you—per application—how long it stayed open, active, and actually visible on screen, its CPU, RAM, Disk, and GPU footprint, and optionally its exact sent and received network traffic broken down by network adapter and remote endpoint.

- **100% Local**: Zero accounts, zero cloud sync, zero telemetry. Data stays in `%LocalAppData%\Radpa` (or portable `Data\` next to the executable).
- **Persian RTL & English LTR**: Built from the ground up with `Vazirmatn UI`, proper RTL `FlowDirection`, and optional Persian digits (`۰-۹`), switchable live to English.
- **Privacy by Default**: Window titles and elevated network packet monitoring are **OFF by default**. Optional AES-256-GCM field encryption protected by Windows DPAPI (`CurrentUser`).

---

## Key Features

### Time & Activity Accuracy
- **Open Duration**: Total wall time at least one process of the app is running.
- **On-Screen Duration**: Visible in the foreground while the display is ON and workstation is unlocked.
- **Active Duration**: On-screen while keyboard/mouse input idle duration is below your configurable threshold.
- **Multi-process Grouping**: Unifies browsers (`chrome.exe`, `msedge.exe`, `firefox.exe`), Electron apps (`Code.exe`), and MSIX packages via customizable priority rules.

### Resource Telemetry (Zero Fake Numbers)
- **CPU %**, **RAM** (Working Set & Private Bytes), **Disk Read/Write** bytes via Win32 source-generated `[LibraryImport]` kernel calls.
- **GPU % & Dedicated VRAM** via Windows WDDM PDH counters (`GPU Engine`, `GPU Process Memory`). When GPU counters are unavailable on a GPU driver or VM, Radpa displays **`N/A`** / **`نامشخص`** honestly.

### Detailed Network & Privacy Auditor (On-Demand Admin Helper)
- Main `Radpa.exe` always runs cleanly as a standard unprivileged user (`asInvoker`).
- Enabling **Detailed Network** spawns `Radpa.NetCollector.exe` via UAC (`Microsoft-Windows-Kernel-Network` ETW, TCP & UDP, IPv4 & IPv6), streaming over an ACL-locked Named Pipe restricted strictly to your user SID:
  - Separate **Sent** and **Received** byte totals (never merged blindly)
  - Interface breakdown (**Ethernet**, **Wi-Fi**, **VPN**, **Loopback**)
  - Remote IP + non-blocking cached reverse DNS hostname
- **Privacy Auditor**: Highlights first-time external destinations contacted in the selected period.

### Productivity, Anomaly Alerts & Exports
- **Rolling Median / MAD Anomaly Alerts**: Detects unusual CPU spikes or upload bursts without heavy ML packages.
- **App Health Score (0–100)**: Transparent breakdown (`Resource 60%` + `Network 25%` + `Startup/Residency 15%`).
- **Project Tagging & Focus Sessions**: Attribute active time by process/path/title rules and track focus sessions against distracting apps.
- **Period Comparison**: Compare Active time, CPU, RAM, and network usage before and after app updates.
- **Hourly Upload Rules**: Notify when an app uploads above N MB/hour (with cooldown).
- **Exports**: CSV, source-generated JSON, and printable RTL/LTR HTML reports.

---

## Quick Start

### From Release Assets
1. Download `Radpa-win-x64.exe` (or `Radpa-win-arm64.exe`) from GitHub Releases.
2. Verify SHA-256 checksum against `SHA256SUMS.txt` (see [docs/ANTIVIRUS.md](docs/ANTIVIRUS.md) regarding unsigned SmartScreen prompts).
3. *(Optional)* To enable detailed network monitoring, also download `Radpa.NetCollector-win-x64.exe` (or `win-arm64`) into the **same folder** as `Radpa.exe`.
4. Run `Radpa.exe` — no .NET runtime installation required.

### Building from Source
Requires **.NET 10 SDK** (`10.0.114` or newer feature patch matching `global.json`):

```powershell
dotnet restore Radpa.sln
dotnet build Radpa.sln -c Release
dotnet test Radpa.sln -c Release
dotnet run --project src/Radpa.UI/Radpa.UI.csproj
```

---

## Documentation
- [System & Data-Flow Architecture](docs/ARCHITECTURE.md)
- [Engineering Decisions & Phase 0 Risk Gates](docs/DECISIONS.md)
- [Privacy Policy & SQLite Schema](docs/PRIVACY.md)
- [Unsigned Binary Verification & Antivirus Notes](docs/ANTIVIRUS.md)
- [Contributing Guide](CONTRIBUTING.md) · [Changelog](CHANGELOG.md) · [Third-Party Notices](THIRD-PARTY-NOTICES.md)

## License
Licensed under the [MIT License](LICENSE). Bundled `Vazirmatn UI` font files are licensed under SIL Open Font License 1.1 (`src/Radpa.UI/Assets/Fonts/OFL.txt`).
