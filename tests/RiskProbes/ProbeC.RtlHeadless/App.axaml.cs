using Avalonia;
using Avalonia.Markup.Xaml;

namespace ProbeC.RtlHeadless;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
