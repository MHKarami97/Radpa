using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using FluentAssertions;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(ProbeC.RtlHeadless.TestAppBuilder))]

namespace ProbeC.RtlHeadless;

public sealed class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class RtlTests
{
    [AvaloniaFact]
    public void RtlFlowDirectionAndMixedBidiLayoutMeasureCleanly()
    {
        var panel = new StackPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Width = 600,
            Children =
            {
                new TextBlock { Text = "برنامه Chrome - مصرف CPU برابر 12.5% و ۱۲۴ مگابایت" },
                new TextBlock { Text = "ترافیک ارسالی: 45.2 MB | IP: 192.168.1.10" },
            },
        };

        var grid = new DataGrid
        {
            FlowDirection = FlowDirection.RightToLeft,
            Width = 600,
            Height = 200,
            AutoGenerateColumns = false,
            ItemsSource = new[]
            {
                new RowItem("گوگل کروم (chrome.exe)", "۱۲.۵٪", "192.168.1.1"),
                new RowItem("وی‌اس‌کد (Code.exe)", "۴.۲٪", "140.82.121.4"),
            },
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "برنامه", Binding = new Avalonia.Data.Binding("Name") });
        grid.Columns.Add(new DataGridTextColumn { Header = "پردازنده", Binding = new Avalonia.Data.Binding("Cpu") });
        grid.Columns.Add(new DataGridTextColumn { Header = "مقصد", Binding = new Avalonia.Data.Binding("Endpoint") });

        var root = new StackPanel { FlowDirection = FlowDirection.RightToLeft, Children = { panel, grid } };
        ToolTip.SetTip(panel, "جزئیات: ارسال 12.4 MB به github.com");

        var window = new Window
        {
            Width = 800,
            Height = 500,
            FlowDirection = FlowDirection.RightToLeft,
            Content = root,
        };

        window.Show();
        window.UpdateLayout();

        window.FlowDirection.Should().Be(FlowDirection.RightToLeft);
        panel.Bounds.Width.Should().BeGreaterThan(0);
        grid.Bounds.Height.Should().Be(200);
    }

    private sealed record RowItem(string Name, string Cpu, string Endpoint);
}
