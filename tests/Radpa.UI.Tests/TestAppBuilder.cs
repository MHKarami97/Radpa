using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Radpa.UI.Tests.TestAppBuilder))]

namespace Radpa.UI.Tests;

public sealed class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<global::Radpa.UI.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
