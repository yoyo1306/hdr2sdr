using System.Runtime.InteropServices;
using Hdr2Sdr.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Hdr2Sdr.App;

public static class Program
{
    public static bool StartMinimizedArg { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        if (!CliRunner.IsGuiLaunch(args))
            return CliRunner.Run(args);

        StartMinimizedArg = args.Length > 0 &&
            (args[0].Equals("--tray", StringComparison.OrdinalIgnoreCase)
             || args[0].Equals("tray", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(initiallyOwned: true, BuildInfo.SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            SignalExistingInstance();
            return 0;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        GC.KeepAlive(mutex);
        return 0;
    }

    private static void SignalExistingInstance()
    {
        uint showMsg = NativeMethods.RegisterWindowMessage(BuildInfo.ShowWindowMessageName);
        if (showMsg == 0)
            return;

        IntPtr existing = FindWindowW(WindowMessageHost.WindowClassName, null);
        if (existing != IntPtr.Zero)
            NativeMethods.PostMessage(existing, showMsg, IntPtr.Zero, IntPtr.Zero);
        else
            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, showMsg, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string lpClassName, string? lpWindowName);
}
