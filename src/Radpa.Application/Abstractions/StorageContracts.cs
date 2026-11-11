using Radpa.Domain.Entities;
using Radpa.Domain.ValueObjects;

namespace Radpa.Application.Abstractions;

public sealed record TelemetryFlushBatch(
    IReadOnlyList<AppIdentity> Apps,
    IReadOnlyList<UsageSession> Sessions,
    IReadOnlyList<ResourceSample> ResourceSamples,
    IReadOnlyList<NetworkSample> NetworkSamples);

public sealed record SensitiveRowRecord(
    long Id,
    string TableName,
    string? WindowTitle,
    string? RemoteIp,
    string? RemoteDomain);

public interface IUsageRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task SaveBatchAsync(TelemetryFlushBatch batch, CancellationToken cancellationToken);

    Task<IReadOnlyList<AppIdentity>> GetAllAppsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<UsageSession>> QuerySessionsAsync(TimeRange range, CancellationToken cancellationToken);

    Task<IReadOnlyList<ResourceSample>> QueryResourceSamplesAsync(TimeRange range, CancellationToken cancellationToken);

    Task<IReadOnlyList<NetworkSample>> QueryNetworkSamplesAsync(TimeRange range, CancellationToken cancellationToken);

    Task<IReadOnlyList<SensitiveRowRecord>> GetAllSensitiveRowsAsync(CancellationToken cancellationToken);

    Task UpdateSensitiveRowsAsync(IReadOnlyList<SensitiveRowRecord> rows, CancellationToken cancellationToken);

    Task<int> ApplyRetentionAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken);

    Task DeleteAppDataAsync(AppKey appKey, CancellationToken cancellationToken);

    Task DeleteAllDataAsync(CancellationToken cancellationToken);

    Task VacuumAsync(CancellationToken cancellationToken);

    Task<long> GetDatabaseSizeBytesAsync(CancellationToken cancellationToken);
}

public interface ISettingsStore
{
    Task<Options.AppSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(Options.AppSettings settings, CancellationToken cancellationToken);
}

public interface IKeyProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);
    byte[] Unprotect(ReadOnlySpan<byte> protectedData);
}

public interface IEncryptionService
{
    bool IsEnabled { get; }
    void Configure(bool enabled, byte[]? protectedKeyBlob);
    byte[] GenerateProtectedKeyBlob();
    string? EncryptField(string? plaintext);
    string? DecryptField(string? ciphertext);
}
