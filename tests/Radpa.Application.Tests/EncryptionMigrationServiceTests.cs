using FluentAssertions;
using NSubstitute;
using Radpa.Application.Abstractions;
using Radpa.Application.Engine;
using Radpa.Application.Options;
using Xunit;

namespace Radpa.Application.Tests;

public sealed class EncryptionMigrationServiceTests
{
    [Fact]
    public async Task MigrateEnableEncryptsSensitiveRowsAndSavesSettings()
    {
        var repo = Substitute.For<IUsageRepository>();
        var encryption = Substitute.For<IEncryptionService>();
        var store = Substitute.For<ISettingsStore>();

        byte[] fakeKeyBlob = [1, 2, 3, 4];
        encryption.GenerateProtectedKeyBlob().Returns(fakeKeyBlob);
        encryption.EncryptField(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>() is { } s ? $"enc:{s}" : null);

        repo.GetAllSensitiveRowsAsync(Arg.Any<CancellationToken>()).Returns([
            new SensitiveRowRecord(1, "resource_samples", "Secret Doc", null, null),
            new SensitiveRowRecord(2, "network_samples", null, "8.8.8.8", "dns.google"),
        ]);

        IReadOnlyList<SensitiveRowRecord>? savedRows = null;
        repo.UpdateSensitiveRowsAsync(Arg.Do<IReadOnlyList<SensitiveRowRecord>>(r => savedRows = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = new EncryptionMigrationService(repo, encryption, store);
        var reports = new List<double>();
        var progress = new Progress<double>(v => reports.Add(v));

        var result = await sut.MigrateAsync(
            new AppSettings { EncryptionEnabled = false },
            enableEncryption: true,
            progress,
            TestContext.Current.CancellationToken);

        result.EncryptionEnabled.Should().BeTrue();
        result.ProtectedEncryptionKeyBase64.Should().Be(Convert.ToBase64String(fakeKeyBlob));
        savedRows.Should().HaveCount(2);
        savedRows![0].WindowTitle.Should().Be("enc:Secret Doc");
        savedRows[1].RemoteIp.Should().Be("enc:8.8.8.8");
        savedRows[1].RemoteDomain.Should().Be("enc:dns.google");

        await store.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.EncryptionEnabled), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MigrateRollsBackEncryptionConfigIfRepositoryThrows()
    {
        var repo = Substitute.For<IUsageRepository>();
        var encryption = Substitute.For<IEncryptionService>();
        var store = Substitute.For<ISettingsStore>();

        byte[] originalBlob = [9, 8, 7];
        var current = new AppSettings
        {
            EncryptionEnabled = true,
            ProtectedEncryptionKeyBase64 = Convert.ToBase64String(originalBlob),
        };

        repo.GetAllSensitiveRowsAsync(Arg.Any<CancellationToken>()).Returns([
            new SensitiveRowRecord(1, "network_samples", null, "enc:1.1.1.1", null),
        ]);
        encryption.DecryptField("enc:1.1.1.1").Returns("1.1.1.1");
        repo.UpdateSensitiveRowsAsync(Arg.Any<IReadOnlyList<SensitiveRowRecord>>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Disk full"));

        var sut = new EncryptionMigrationService(repo, encryption, store);

        var act = async () => await sut.MigrateAsync(current, enableEncryption: false, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        encryption.Received().Configure(true, Arg.Is<byte[]?>(b => b != null && b.SequenceEqual(originalBlob)));
        await store.DidNotReceive().SaveAsync(Arg.Any<AppSettings>(), Arg.Any<CancellationToken>());
    }
}
