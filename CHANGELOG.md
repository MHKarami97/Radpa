# Changelog

All notable changes to **Radpa (ردپا)** are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Clean Modular Monolith architecture (`Radpa.Domain` <- `Radpa.Application` <- `Radpa.Infrastructure` <- `Radpa.UI`, plus isolated `Radpa.NetCollector` ETW helper).
- Per-application tracking: Open, Active (foreground + input < idle threshold), and On-Screen (visible foreground + display on + unlocked) durations.
- Low-overhead Windows collectors: Toolhelp32 process discovery, `SetWinEventHook` foreground tracking, `GetLastInputInfo` idle detection, power/session lock monitoring, per-process CPU/RAM/Disk I/O via Win32 `[LibraryImport]`, and WDDM GPU/VRAM PDH counters (reporting `"N/A"` honestly when unavailable).
- Optional on-demand elevated `Radpa.NetCollector.exe` for ETW (`Microsoft-Windows-Kernel-Network`) per-app sent and received byte breakdown by interface (`Ethernet`, `Wi-Fi`, `VPN`, `Loopback`) and remote IP / cached reverse DNS.
- Multi-process grouping rules, project tagging rules, Privacy Auditor (highlighting first-time remote endpoints), rolling median/MAD anomaly detection, deterministic App Health Score breakdown, Focus Sessions, period comparison, hourly upload alert rules, and CSV / source-generated JSON / HTML report export.
- Local-only SQLite storage (WAL mode, parameterized ADO.NET) with `%LocalAppData%\Radpa` and Portable (`Data\`) modes, retention pruning, delete-app/all controls, vacuuming, and optional AES-256-GCM encryption with DPAPI key protection and transactional migration.
- Persian RTL default UI (`Vazirmatn UI`) with optional Persian digits (`۰-۹`), English LTR switching, Dark default + Light optional theme, dual-process Win32 tray host (`--collector`) + disposable Avalonia UI (`--ui`), single-instance mutex/wake event, and per-user startup registration.
- GitHub Releases update checker using `ETag` conditional requests, rate-limit backoff, and "Skip this version".
- xUnit v3, Avalonia Headless UI/RTL, Windows live collector tests, BenchmarkDotNet benchmarks, GitHub Actions CI & release pipelines with SHA-256 checksums and build provenance attestation.
