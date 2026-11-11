using FluentAssertions;
using Radpa.Domain.Entities;
using Radpa.Domain.Services;
using Radpa.Domain.ValueObjects;
using Xunit;

namespace Radpa.Domain.Tests;

public sealed class GroupingAndTaggingTests
{
    [Fact]
    public void AppKeyNormalizesCaseAndWhitespace()
    {
        var a = new AppKey("  Group:Google Chrome ");
        var b = new AppKey("group:google chrome");

        a.Should().Be(b);
        a.Value.Should().Be("group:google chrome");
    }

    [Fact]
    public void TimeRangeValidatesBoundsAndComputesTodayAndIntersection()
    {
        var t0 = new DateTimeOffset(2026, 11, 10, 14, 30, 0, TimeSpan.FromHours(3.5));
        var today = TimeRange.Today(t0);

        today.StartUtc.Should().Be(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.FromHours(3.5)).ToUniversalTime());
        today.EndUtc.Should().Be(t0.ToUniversalTime());
        today.Contains(today.StartUtc).Should().BeTrue();
        today.Contains(today.EndUtc).Should().BeFalse();

        var overlap = today.Intersect(new TimeRange(today.StartUtc.AddHours(2), today.EndUtc.AddHours(2)));
        overlap.Should().NotBeNull();
        overlap!.Value.StartUtc.Should().Be(today.StartUtc.AddHours(2));
        overlap.Value.EndUtc.Should().Be(today.EndUtc);

        var act = () => new TimeRange(t0, t0.AddMinutes(-1));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GroupingResolverMatchesByPriorityAndMatchKinds()
    {
        var rules = new[]
        {
            new GroupingRule("low", "Chrome Generic", GroupingMatchKind.ExecutableName, "chrome.exe", "Browsers", true, 50),
            new GroupingRule("disabled", "Disabled Rule", GroupingMatchKind.ExecutableName, "chrome.exe", "Ignored", false, 1),
            new GroupingRule("high", "Work Chrome", GroupingMatchKind.DirectoryPrefix, @"C:\Work\Chrome", "Work Browser", true, 10),
            new GroupingRule("pkg", "Store Terminal", GroupingMatchKind.PackageFamily, "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "Windows Terminal", true, 20),
            new GroupingRule("exact", "Notepad Exact", GroupingMatchKind.ExecutablePath, @"C:\Windows\System32\notepad.exe", "Notepad Group", true, 30),
        };

        var resolver = new GroupingResolver(rules);

        var workChrome = resolver.Resolve(@"C:\Work\Chrome\chrome.exe");
        workChrome.Key.Value.Should().Be("group:work browser");
        workChrome.DisplayName.Should().Be("Work Browser");

        var regularChrome = resolver.Resolve(@"C:\Program Files\Google\Chrome\Application\chrome.exe");
        regularChrome.Key.Value.Should().Be("group:browsers");

        var terminal = resolver.Resolve(@"C:\Program Files\WindowsApps\wt.exe", "Microsoft.WindowsTerminal_8wekyb3d8bbwe");
        terminal.Key.Value.Should().Be("group:windows terminal");

        var notepad = resolver.Resolve(@"C:\Windows\System32\notepad.exe");
        notepad.Key.Value.Should().Be("group:notepad group");

        var fallbackPkg = resolver.Resolve(@"C:\Apps\calc.exe", "Microsoft.WindowsCalculator_8wekyb3d8bbwe", "Calculator");
        fallbackPkg.Key.Value.Should().Be("pkg:microsoft.windowscalculator_8wekyb3d8bbwe");
        fallbackPkg.DisplayName.Should().Be("Calculator");

        var fallbackExe = resolver.Resolve(@"C:\Tools\myTool.exe");
        fallbackExe.Key.Value.Should().Be(@"exe:c:\tools\mytool.exe");
        fallbackExe.DisplayName.Should().Be("MyTool");
    }

    [Fact]
    public void ProjectTagMatcherEvaluatesProcessPathAndWindowTitleRules()
    {
        var app = new AppIdentity(new AppKey("group:vscode"), "Visual Studio Code", @"D:\Tools\VSCode\Code.exe");
        var rules = new[]
        {
            new ProjectTagRule("off", "Inactive", ProjectMatchKind.ProcessName, "Code", IsEnabled: false),
            new ProjectTagRule("title", "Radpa App", ProjectMatchKind.WindowTitleContains, "radpa"),
            new ProjectTagRule("path", "ToolsWork", ProjectMatchKind.ExecutablePath, @"D:\Tools"),
        };

        var matcher = new ProjectTagMatcher(rules);

        matcher.Match(app, "MainWindow.axaml - radpa - Visual Studio Code").Should().Be("Radpa App");
        matcher.Match(app, "Other Workspace").Should().Be("ToolsWork");
        matcher.Match(app, null).Should().Be("ToolsWork");

        var otherApp = new AppIdentity(new AppKey("exe:notepad"), "Notepad", @"C:\Windows\notepad.exe");
        matcher.Match(otherApp, null).Should().BeNull();
    }
}
