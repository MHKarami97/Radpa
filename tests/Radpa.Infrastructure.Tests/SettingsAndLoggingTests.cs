using FluentAssertions;
using Radpa.Application.Options;
using Radpa.Domain.Entities;
using Radpa.Infrastructure.Logging;
using Radpa.Infrastructure.Persistence;
using Xunit;

namespace Radpa.Infrastructure.Tests;

public sealed class SettingsAndLoggingTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsAndLoggingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"radpa-cfg-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task JsonSettingsStoreLoadsDefaultsWhenMissingAndRoundTripsAotJson()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        var store = new JsonSettingsStore(path);

        var defaults = await store.LoadAsync(CancellationToken.None);
        defaults.Language.Should().Be(UiLanguage.Persian);
        defaults.RecordWindowTitles.Should().BeFalse();

        var custom = defaults with
        {
            Language = UiLanguage.English,
            Theme = UiThemeMode.Light,
            RetentionDays = 30,
            ProjectRules = [new ProjectTagRule("p1", "Radpa", ProjectMatchKind.ProcessName, "Code")],
        };

        await store.SaveAsync(custom, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);
        loaded.Language.Should().Be(UiLanguage.English);
        loaded.Theme.Should().Be(UiThemeMode.Light);
        loaded.RetentionDays.Should().Be(30);
        loaded.ProjectRules.Should().ContainSingle().Which.ProjectName.Should().Be("Radpa");

        // Corrupt JSON falls back cleanly to defaults
        await File.WriteAllTextAsync(path, "{ invalid json");
        var fallback = await store.LoadAsync(CancellationToken.None);
        fallback.Language.Should().Be(UiLanguage.Persian);
    }

    [Fact]
    public void StoragePathResolverResolvesPortableVsLocalAppData()
    {
        var portable = StoragePathResolver.ResolveDataDirectory(portableOverride: true);
        portable.Should().EndWith("Data");

        var local = StoragePathResolver.ResolveDataDirectory(portableOverride: false);
        local.Should().EndWith("Radpa");
    }

    [Fact]
    public void RollingFileLoggerWritesStructuredEntriesAndSupportsExceptions()
    {
        var logDir = Path.Combine(_tempDir, "logs");
        using (var logger = new RollingFileLogger(logDir))
        {
            logger.Info("Started collector");
            logger.Warning("Low disk");
            logger.LogError("Failure", new InvalidOperationException("Boom"));
        }

        var logFile = Path.Combine(logDir, "radpa.log");
        File.Exists(logFile).Should().BeTrue();
        var text = File.ReadAllText(logFile);
        text.Should().Contain("[INFO] Started collector");
        text.Should().Contain("[WARN] Low disk");
        text.Should().Contain("[ERROR] Failure | InvalidOperationException: Boom");
    }
}
