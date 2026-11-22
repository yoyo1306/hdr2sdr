using System.Runtime.InteropServices;
using Hdr2Sdr.Core;

namespace Hdr2Sdr.App;

/// <summary>
/// Pure Win32 invisible host window for:
/// - System tray icon (Shell_NotifyIconW + native context menu + Explorer restart recovery)
/// - Global hotkeys (WM_HOTKEY)
/// - System display/power/session events (WM_DISPLAYCHANGE, WM_POWERBROADCAST, WM_WTSSESSION_CHANGE)
/// - Single-instance focus (WM_HDR2SDR_SHOW) and CLI sync (WM_HDR2SDR_SYNC)
/// </summary>
public sealed class WindowMessageHost : IDisposable
{
    public const string WindowClassName = "Hdr2SdrMessageWindow";

    public const int HotkeyToggleHdr = 1;
    public const int HotkeySdrUp = 2;
    public const int HotkeySdrDown = 3;
    public const int HotkeyProfileJour = 4;
    public const int HotkeyProfileSoir = 5;
    public const int HotkeyProfileJeu = 6;

    private const uint WM_APP_TRAY = 0x8000 + 42;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_NULL = 0x0000;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_SHOWTIP = 0x00000080;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;
    private const uint NOTIFYICON_VERSION_4 = 4;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint LR_DEFAULTSIZE = 0x00000040;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint MF_CHECKED = 0x00000008;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_BOTTOMALIGN = 0x0020;

    private const int CMD_OPEN = 1001;
    private const int CMD_TOGGLE_HDR = 1002;
    private const int CMD_PROFILE_JOUR = 1003;
    private const int CMD_PROFILE_SOIR = 1004;
    private const int CMD_PROFILE_JEU = 1005;
    private const int CMD_QUIT = 1006;

    private readonly WndProcDelegate _wndProcDelegate;
    private readonly uint _showWindowMsg;
    private readonly uint _syncStateMsg;
    private readonly uint _taskbarCreatedMsg;

    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _trayWanted;
    private bool _trayAdded;
    private bool _wtsRegistered;
    private bool _disposed;

    private string _tooltip = "Hdr2sdr";
    private bool _hdrOn;
    private int _soirPercent;
    private int _jeuPercent = 80;

    public event Action<int>? HotkeyPressed;
    public event Action? ShowWindowRequested;
    public event Action? SyncStateRequested;
    public event Action? SystemTopologyOrSessionChanged;
    public event Action? TrayToggleHdrRequested;
    public event Action<int>? TrayProfileRequested;
    public event Action? QuitRequested;

    public WindowMessageHost(string iconPath)
    {
        _wndProcDelegate = WndProc;
        _showWindowMsg = NativeMethods.RegisterWindowMessage(BuildInfo.ShowWindowMessageName);
        _syncStateMsg = NativeMethods.RegisterWindowMessage(BuildInfo.SyncStateMessageName);
        _taskbarCreatedMsg = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        IntPtr hInstance = GetModuleHandleW(null);
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = hInstance,
            lpszClassName = WindowClassName
        };

        RegisterClassExW(ref wc);
        _hwnd = CreateWindowExW(
            0,
            WindowClassName,
            "hdr2sdr host",
            0x80000000u, // WS_POPUP (hidden top-level window so broadcast & system events arrive)
            0, 0, 0, 0,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_hwnd != IntPtr.Zero)
        {
            _wtsRegistered = NativeMethods.WTSRegisterSessionNotification(_hwnd, NativeMethods.NOTIFY_FOR_THIS_SESSION);
        }

        LoadIcon(iconPath);
    }

    public IntPtr Handle => _hwnd;

    public void SetTrayVisible(bool visible)
    {
        _trayWanted = visible;
        if (visible)
        {
            if (!_trayAdded)
                AddTrayIcon();
        }
        else
        {
            RemoveTrayIcon();
        }
    }

    public bool RegisterHotkey(int id, string? chord, bool allowRepeat, out int win32Error)
    {
        win32Error = 0;
        if (_hwnd == IntPtr.Zero)
            return false;

        NativeMethods.UnregisterHotKey(_hwnd, id);
        if (!HotkeyParser.TryParse(chord, out uint mods, out uint vk))
            return false;

        if (!allowRepeat)
            mods |= NativeMethods.MOD_NOREPEAT;

        bool ok = NativeMethods.RegisterHotKey(_hwnd, id, mods, vk);
        if (!ok)
            win32Error = Marshal.GetLastWin32Error();
        return ok;
    }

    public void UnregisterHotkey(int id)
    {
        if (_hwnd != IntPtr.Zero)
            NativeMethods.UnregisterHotKey(_hwnd, id);
    }

    public void UnregisterAllHotkeys()
    {
        for (int id = HotkeyToggleHdr; id <= HotkeyProfileJeu; id++)
            UnregisterHotkey(id);
    }

    public void UpdateTrayState(string tooltip, bool hdrOn, int soirPercent, int jeuPercent)
    {
        _tooltip = string.IsNullOrWhiteSpace(tooltip) ? "hdr2sdr" : tooltip;
        if (_tooltip.Length > 127)
            _tooltip = _tooltip[..127];
        _hdrOn = hdrOn;
        _soirPercent = soirPercent;
        _jeuPercent = jeuPercent;

        if (!_trayAdded || _hwnd == IntPtr.Zero)
            return;

        var nid = CreateBaseNotifyData();
        nid.uFlags = NIF_TIP | NIF_SHOWTIP;
        nid.szTip = _tooltip;
        Shell_NotifyIconW(NIM_MODIFY, ref nid);
    }

    private void LoadIcon(string iconPath)
    {
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            _hIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
        }

        if (_hIcon == IntPtr.Zero)
        {
            _hIcon = LoadImageW(IntPtr.Zero, "#32512", IMAGE_ICON, 0, 0, LR_DEFAULTSIZE);
        }
    }

    private void AddTrayIcon()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        var nid = CreateBaseNotifyData();
        nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        nid.uCallbackMessage = WM_APP_TRAY;
        nid.hIcon = _hIcon;
        nid.szTip = _tooltip;

        if (Shell_NotifyIconW(NIM_ADD, ref nid))
        {
            _trayAdded = true;
            nid.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
            Shell_NotifyIconW(NIM_SETVERSION, ref nid);
        }
    }

    private void RemoveTrayIcon()
    {
        if (!_trayAdded || _hwnd == IntPtr.Zero)
            return;

        var nid = CreateBaseNotifyData();
        Shell_NotifyIconW(NIM_DELETE, ref nid);
        _trayAdded = false;
    }

    private NOTIFYICONDATAW CreateBaseNotifyData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = 1,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty
    };

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            HotkeyPressed?.Invoke(id);
            return IntPtr.Zero;
        }

        if (msg == WM_APP_TRAY)
        {
            uint mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
            {
                ShowWindowRequested?.Invoke();
            }
            else if (mouseMsg is WM_RBUTTONUP or WM_CONTEXTMENU)
            {
                ShowContextMenu();
            }
            return IntPtr.Zero;
        }

        if (_showWindowMsg != 0 && msg == _showWindowMsg)
        {
            ShowWindowRequested?.Invoke();
            return IntPtr.Zero;
        }

        if (_syncStateMsg != 0 && msg == _syncStateMsg)
        {
            SyncStateRequested?.Invoke();
            return IntPtr.Zero;
        }

        if (_taskbarCreatedMsg != 0 && msg == _taskbarCreatedMsg)
        {
            _trayAdded = false;
            if (_trayWanted)
                AddTrayIcon();
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_DISPLAYCHANGE)
        {
            SystemTopologyOrSessionChanged?.Invoke();
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_POWERBROADCAST)
        {
            int pbt = wParam.ToInt32();
            if (pbt is NativeMethods.PBT_APMRESUMEAUTOMATIC or NativeMethods.PBT_APMRESUMESUSPEND)
                SystemTopologyOrSessionChanged?.Invoke();
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_WTSSESSION_CHANGE)
        {
            if (wParam.ToInt32() == NativeMethods.WTS_SESSION_UNLOCK)
                SystemTopologyOrSessionChanged?.Invoke();
            return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return;

        try
        {
            AppendMenuW(menu, MF_STRING, CMD_OPEN, "Ouvrir Hdr2sdr");
            AppendMenuW(menu, MF_STRING, CMD_TOGGLE_HDR, _hdrOn ? "Basculer SDR (HDR OFF)" : "Basculer HDR (HDR ON)");
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING | (!_hdrOn ? MF_CHECKED : 0u), CMD_PROFILE_JOUR, "Profil Jour (SDR natif)");
            AppendMenuW(menu, MF_STRING, CMD_PROFILE_SOIR, $"Profil Soir (HDR • {_soirPercent} %)");
            AppendMenuW(menu, MF_STRING, CMD_PROFILE_JEU, $"Profil Jeu (HDR • {_jeuPercent} %)");
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, CMD_QUIT, "Quitter");

            GetCursorPos(out POINT pt);
            SetForegroundWindow(_hwnd);
            int cmd = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_BOTTOMALIGN, pt.x, pt.y, _hwnd, IntPtr.Zero);
            NativeMethods.PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (cmd)
            {
                case CMD_OPEN:
                    ShowWindowRequested?.Invoke();
                    break;
                case CMD_TOGGLE_HDR:
                    TrayToggleHdrRequested?.Invoke();
                    break;
                case CMD_PROFILE_JOUR:
                    TrayProfileRequested?.Invoke(0);
                    break;
                case CMD_PROFILE_SOIR:
                    TrayProfileRequested?.Invoke(1);
                    break;
                case CMD_PROFILE_JEU:
                    TrayProfileRequested?.Invoke(2);
                    break;
                case CMD_QUIT:
                    QuitRequested?.Invoke();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        UnregisterAllHotkeys();
        RemoveTrayIcon();

        if (_wtsRegistered && _hwnd != IntPtr.Zero)
        {
            NativeMethods.WTSUnRegisterSessionNotification(_hwnd);
            _wtsRegistered = false;
        }

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
