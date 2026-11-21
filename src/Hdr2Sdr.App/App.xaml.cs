using Microsoft.UI.Xaml;

namespace Hdr2Sdr.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(Program.StartMinimizedArg);
        _window.InitializeAndMaybeShow();
    }
}
