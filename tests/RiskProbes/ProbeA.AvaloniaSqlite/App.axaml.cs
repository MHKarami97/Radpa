using Avalonia;
using Avalonia.Markup.Xaml;

namespace ProbeA.AvaloniaSqlite;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
