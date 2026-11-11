using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Radpa.UI.Views;

namespace Radpa.UI;

public sealed partial class App : global::Avalonia.Application
{
    internal Func<MainWindow>? WindowFactory { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && WindowFactory is not null)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = WindowFactory();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
