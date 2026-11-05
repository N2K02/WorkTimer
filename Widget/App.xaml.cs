using WidBar.SDK;
using WidBar.SDK.Hosting;

namespace WorkTimer.Widget;

public partial class App : WidgetHostApplication
{
    public App() => InitializeComponent();
    protected override IWidgetPlugin CreatePlugin() => new ClockPlugin();
}
