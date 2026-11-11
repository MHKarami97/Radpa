# Privacy Policy & Local Storage Specification (`docs/PRIVACY.md`)

**Short version:** Radpa never creates accounts, never phones home, never collects analytics/telemetry, never logs keystrokes, and never captures screenshots or window contents.

---

## 1. Where Your Data Lives
- **Standard mode (default):** `%LocalAppData%\Radpa\`
  - `radpa.db` (+ `-wal` / `-shm`) — Telemetry SQLite database
  - `settings.json` — Preferences, rules, update metadata
  - `logs\radpa.log` (capped at 2 MB x up to 3 rotated archives)
- **Portable mode:** If toggled ON in Settings (creating a `radpa.portable` marker beside `Radpa.exe`), all data is stored in `Data\` next to the executable instead of `%LocalAppData%`.

---

## 2. Exact Data Recorded in SQLite (`radpa.db`)

Schema defined cleanly in [`SqliteSchemaMigrator.cs`](../src/Radpa.Infrastructure/Persistence/SqliteSchemaMigrator.cs):

| Table | Columns | Notes |
|---|---|---|
| `apps` | `app_key`, `display_name`, `executable_path`, `package_family_name`, `publisher` | Canonical app catalog (merged by grouping rules) |
| `usage_sessions` | `id`, `app_key`, `start_utc_ticks`, `end_utc_ticks`, `open_seconds`, `active_seconds`, `on_screen_seconds`, `project_tag` | Durations in seconds; `project_tag` set only by local rules |
| `resource_samples` | `id`, `app_key`, `timestamp_utc_ticks`, `cpu_percent`, `working_set_bytes`, `private_bytes`, `disk_read_bytes`, `disk_write_bytes`, `gpu_percent`, `gpu_vram_bytes`, `window_title` | `gpu_*` nullable (`N/A` if unsupported); **`window_title` is always `NULL` unless explicitly enabled** |
| `network_samples` | `id`, `app_key`, `timestamp_utc_ticks`, `interface_name`, `interface_kind`, `protocol`, `remote_ip`, `remote_domain`, `bytes_sent`, `bytes_received` | Only populated when the user opts into **Detailed Network Mode** + grants UAC |
| `schema_version` | `version`, `applied_utc` | Migration version number |

### Disabled by Default (Strict Privacy Defaults)
- **Window titles (`RecordWindowTitles = false`)**: Active window titles are **not stored** unless you manually opt in (for example, to drive window-title project tagging).
- **Detailed network capture (`DetailedNetworkEnabled = false`)**: Kernel-Network ETW packet metadata is not collected until you explicitly enable it and accept UAC.

---

## 3. Optional Local Encryption (AES-256-GCM + Windows DPAPI)
When **Encrypt sensitive fields** is enabled in Settings:
1. A 256-bit random AES key is generated locally and sealed to your current Windows user account via `ProtectedData.Protect` (DPAPI `CurrentUser` scope), stored in `settings.json` (`protectedEncryptionKeyBase64`).
2. `EncryptionMigrationService` transactionally encrypts existing `window_title`, `remote_ip`, and `remote_domain` columns using `AES-256-GCM` (`ENC1:<base64(nonce|tag|ciphertext)>`).
3. If migration fails at any step, the SQLite transaction rolls back atomically and settings remain untouched.
4. Toggling encryption back OFF decrypts existing values transactionally.

---

## 4. Network Connections Made by Radpa
- **Zero telemetry or cloud endpoints.**
- **Reverse DNS (local DNS resolver only, when Detailed Network is active):** Remote IPs captured by ETW are resolved to PTR domain names via OS DNS (`Dns.GetHostEntryAsync`) and cached strictly in RAM (`ReverseDnsCache`). No third-party GeoIP/cloud APIs are contacted.
- **Optional GitHub Release Check (`UpdateCheck:Enabled`):** When enabled (default: once per 24 hours), Radpa sends a single read-only HTTPS `GET` to:
  `https://api.github.com/repos/{UpdateCheck:Repository}/releases/latest`
  using `If-None-Match` (`ETag`) conditional requests and a clean `User-Agent: Radpa/<version>`. You can disable this completely in Settings -> Update Check.

---

## 5. User Control & Erasure
In **Settings -> Privacy & Data Controls**, you can at any time:
- Set **Retention Days** (e.g., 30 / 90 days) to automatically purge older records.
- **Delete selected app data** (cascades cleanly via `ON DELETE CASCADE`).
- **Delete all local history** (`DELETE` across all tables).
- **Compact & Vacuum Database** (`PRAGMA wal_checkpoint(TRUNCATE); VACUUM;`).
- Uninstall cleanly by simply deleting the executable and `%LocalAppData%\Radpa\` (plus disabling *Start with Windows*, which only removes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Radpa`).
