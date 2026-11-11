using System.Text.Json;
using System.Text.Json.Serialization;
using Radpa.Application.Abstractions;
using Radpa.Application.Options;

namespace Radpa.Infrastructure.Persistence;

public static class StoragePathResolver
{
    private const string PortableMarkerFile = "radpa.portable";

    public static string ResolveDataDirectory(bool? portableOverride = null)
    {
        var baseDir = AppContext.BaseDirectory;
        var markerPath = Path.Combine(baseDir, PortableMarkerFile);
        var isPortable = portableOverride ?? File.Exists(markerPath);

        if (isPortable)
        {
            var portableData = Path.Combine(baseDir, "Data");
            Directory.CreateDirectory(portableData);
            return portableData;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDir = Path.Combine(localAppData, "Radpa");
        Directory.CreateDirectory(appDir);
        return appDir;
    }

    public static void SetPortableMarker(bool enabled)
    {
        var markerPath = Path.Combine(AppContext.BaseDirectory, PortableMarkerFile);
        if (enabled)
        {
            File.WriteAllText(markerPath, "portable=true");
        }
        else if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
public partial class SettingsJsonContext : JsonSerializerContext;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(StoragePathResolver.ResolveDataDirectory(), "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            await using var fs = File.OpenRead(_settingsPath);
            var loaded = await JsonSerializer.DeserializeAsync(fs, SettingsJsonContext.Default.AppSettings, cancellationToken);
            return loaded ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tempPath = _settingsPath + ".tmp";
            await using (var fs = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(fs, settings, SettingsJsonContext.Default.AppSettings, cancellationToken);
            }

            File.Move(tempPath, _settingsPath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
