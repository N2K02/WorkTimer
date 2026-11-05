using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace WorkTimer.Widget;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }
}
