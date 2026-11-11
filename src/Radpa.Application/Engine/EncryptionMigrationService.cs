using Radpa.Application.Abstractions;
using Radpa.Application.Options;

namespace Radpa.Application.Engine;

/// <summary>
/// Safely migrates existing sensitive SQLite columns (window titles, IPs, domains) between plaintext and AES-GCM encrypted state,
/// rolling back cleanly if any error occurs.
/// </summary>
public sealed class EncryptionMigrationService
{
    private readonly IUsageRepository _repository;
    private readonly IEncryptionService _encryption;
    private readonly ISettingsStore _settingsStore;

    public EncryptionMigrationService(
        IUsageRepository repository,
        IEncryptionService encryption,
        ISettingsStore settingsStore)
    {
        _repository = repository;
        _encryption = encryption;
        _settingsStore = settingsStore;
    }

    public async Task<AppSettings> MigrateAsync(
        AppSettings currentSettings,
        bool enableEncryption,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);
        if (currentSettings.EncryptionEnabled == enableEncryption)
        {
            return currentSettings;
        }

        var originalBlob = string.IsNullOrWhiteSpace(currentSettings.ProtectedEncryptionKeyBase64)
            ? null
            : Convert.FromBase64String(currentSettings.ProtectedEncryptionKeyBase64);

        byte[]? newBlob = originalBlob;
        if (enableEncryption && newBlob is null)
        {
            newBlob = _encryption.GenerateProtectedKeyBlob();
        }

        var rows = await _repository.GetAllSensitiveRowsAsync(cancellationToken);
        var updatedRows = new List<SensitiveRowRecord>(rows.Count);

        try
        {
            if (enableEncryption)
            {
                _encryption.Configure(true, newBlob);
                for (var i = 0; i < rows.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = rows[i];
                    updatedRows.Add(row with
                    {
                        WindowTitle = _encryption.EncryptField(row.WindowTitle),
                        RemoteIp = _encryption.EncryptField(row.RemoteIp),
                        RemoteDomain = _encryption.EncryptField(row.RemoteDomain),
                    });

                    if (rows.Count > 0 && i % 50 == 0)
                    {
                        progress?.Report((double)(i + 1) / rows.Count * 0.7);
                    }
                }
            }
            else
            {
                _encryption.Configure(true, originalBlob);
                for (var i = 0; i < rows.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = rows[i];
                    updatedRows.Add(row with
                    {
                        WindowTitle = _encryption.DecryptField(row.WindowTitle),
                        RemoteIp = _encryption.DecryptField(row.RemoteIp),
                        RemoteDomain = _encryption.DecryptField(row.RemoteDomain),
                    });

                    if (rows.Count > 0 && i % 50 == 0)
                    {
                        progress?.Report((double)(i + 1) / rows.Count * 0.7);
                    }
                }
            }

            await _repository.UpdateSensitiveRowsAsync(updatedRows, cancellationToken);
            progress?.Report(0.9);

            var updatedSettings = currentSettings with
            {
                EncryptionEnabled = enableEncryption,
                ProtectedEncryptionKeyBase64 = enableEncryption && newBlob is not null
                    ? Convert.ToBase64String(newBlob)
                    : currentSettings.ProtectedEncryptionKeyBase64,
            };

            _encryption.Configure(enableEncryption, newBlob);
            await _settingsStore.SaveAsync(updatedSettings, cancellationToken);
            progress?.Report(1.0);
            return updatedSettings;
        }
        catch
        {
            // Restore encryption configuration state on failure; repository transaction rolls back DB rows atomically.
            _encryption.Configure(currentSettings.EncryptionEnabled, originalBlob);
            throw;
        }
    }
}
