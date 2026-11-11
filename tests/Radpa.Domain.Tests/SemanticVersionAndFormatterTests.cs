using FluentAssertions;
using Radpa.Domain.Services;
using Xunit;

namespace Radpa.Domain.Tests;

public sealed class SemanticVersionAndFormatterTests
{
    [Fact]
    public void SemanticVersionParsesValidTagsAndRejectsInvalidOnes()
    {
        SemanticVersion.TryParse("v2.4.1", out var v1).Should().BeTrue();
        v1.Major.Should().Be(2);
        v1.Minor.Should().Be(4);
        v1.Patch.Should().Be(1);
        v1.IsPreRelease.Should().BeFalse();

        SemanticVersion.TryParse("1.0.0-rc.2+build99", out var v2).Should().BeTrue();
        v2.PreRelease.Should().Be("rc.2");
        v2.IsPreRelease.Should().BeTrue();
        v2.ToString().Should().Be("1.0.0-rc.2");

        SemanticVersion.TryParse("1.2", out _).Should().BeFalse();
        SemanticVersion.TryParse("1.2.3-", out _).Should().BeFalse();
        SemanticVersion.TryParse("", out _).Should().BeFalse();
    }

    [Fact]
    public void SemanticVersionComparesFollowingSemVerPrecedence()
    {
        SemanticVersion.TryParse("1.0.0-1", out var numPre).Should().BeTrue();
        SemanticVersion.TryParse("1.0.0-alpha", out var alpha).Should().BeTrue();
        SemanticVersion.TryParse("1.0.0-alpha.1", out var alpha1).Should().BeTrue();
        SemanticVersion.TryParse("1.0.0-beta", out var beta).Should().BeTrue();
        SemanticVersion.TryParse("1.0.0", out var stable).Should().BeTrue();
        SemanticVersion.TryParse("1.0.1", out var nextPatch).Should().BeTrue();

        (numPre < alpha).Should().BeTrue();
        (alpha < alpha1).Should().BeTrue();
        (alpha1 < beta).Should().BeTrue();
        (beta < stable).Should().BeTrue();
        (stable < nextPatch).Should().BeTrue();
    }

    [Fact]
    public void NumberFormatterFormatsBytesDurationsAndPercentInPersianAndEnglish()
    {
        NumberFormatter.ToPersianDigits("App 2026-11-10: 45%").Should().Be("App ۲۰۲۶-۱۱-۱۰: ۴۵%");

        var bytesEn = NumberFormatter.FormatBytes(1536, usePersianDigits: false, persianLocale: false);
        bytesEn.Should().Be("\u200E1.5 KB");

        var bytesFa = NumberFormatter.FormatBytes(2L * 1024 * 1024, usePersianDigits: true, persianLocale: true);
        bytesFa.Should().Be("۲ مگابایت");

        var durEn = NumberFormatter.FormatDuration(TimeSpan.FromMinutes(65), usePersianDigits: false, persianLocale: false);
        durEn.Should().Be("1h 5m");

        var durFa = NumberFormatter.FormatDuration(TimeSpan.FromSeconds(125), usePersianDigits: true, persianLocale: true);
        durFa.Should().Be("۲ دقیقه و ۵ ثانیه");

        NumberFormatter.FormatPercent(null, true, "نامشخص").Should().Be("نامشخص");
        NumberFormatter.FormatPercent(42.5, true).Should().Be("۴۲٫۵٪");
        NumberFormatter.FormatPercent(42.5, false).Should().Be("\u200E42.5%");
    }
}
