using System.Security.Cryptography;
using FluentAssertions;
using NSubstitute;
using Radpa.Application.Abstractions;
using Radpa.Infrastructure.Security;
using Xunit;

namespace Radpa.Infrastructure.Tests;

public sealed class EncryptionTests
{
    [Fact]
    public void AesGcmEncryptionServiceEncryptsAndDecryptsRoundTripAndPassesThroughWhenDisabled()
    {
        var protector = new DpapiKeyProtector();
        var service = new AesGcmEncryptionService(protector);

        service.IsEnabled.Should().BeFalse();
        service.EncryptField("1.1.1.1").Should().Be("1.1.1.1");

        var blob = service.GenerateProtectedKeyBlob();
        service.Configure(true, blob);
        service.IsEnabled.Should().BeTrue();

        var cipher = service.EncryptField("Secret Window Title - فارسی");
        cipher.Should().StartWith("ENC1:");
        cipher.Should().NotContain("Secret");

        // Double encryption is a no-op
        service.EncryptField(cipher).Should().Be(cipher);

        service.DecryptField(cipher).Should().Be("Secret Window Title - فارسی");
        service.DecryptField(null).Should().BeNull();
        service.DecryptField("plaintext-ip").Should().Be("plaintext-ip");
    }

    [Fact]
    public void AesGcmDecryptionWithWrongKeyThrowsCryptographicException()
    {
        var protector = Substitute.For<IKeyProtector>();
        byte[] keyA = new byte[32];
        byte[] keyB = new byte[32];
        RandomNumberGenerator.Fill(keyA);
        RandomNumberGenerator.Fill(keyB);

        protector.Protect(Arg.Any<ReadOnlySpan<byte>>()).Returns(ci => ci.Arg<ReadOnlySpan<byte>>().ToArray());
        protector.Unprotect(Arg.Is<ReadOnlySpan<byte>>(b => b.SequenceEqual(keyA))).Returns(keyA);
        protector.Unprotect(Arg.Is<ReadOnlySpan<byte>>(b => b.SequenceEqual(keyB))).Returns(keyB);

        var s1 = new AesGcmEncryptionService(protector);
        s1.Configure(true, keyA);
        var encrypted = s1.EncryptField("8.8.8.8");

        var s2 = new AesGcmEncryptionService(protector);
        s2.Configure(true, keyB);

        var act = () => s2.DecryptField(encrypted);
        act.Should().Throw<CryptographicException>();
    }
}
