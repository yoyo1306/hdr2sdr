using System.Diagnostics;
using System.Runtime.InteropServices;
using Hdr2Sdr.Core;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using WinRT.Interop;

namespace Hdr2Sdr.App;

public sealed partial class MainWindow : Window
{
    private const int WindowWidthDips = 500;
    private const int WindowHeightDips = 556;

    private readonly AppConfig _cfg;
    private readonly bool _startMinimizedArg;
    private readonly WindowMessageHost _host;
    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;

    // Coalesced background brightness writer (prevents DDC/CI saturation when dragging slider or holding hotkey)
    private readonly object _writeSync = new();
    private readonly AutoResetEvent _writeEvent = new(false);
    private readonly Thread _writeThread;
    private volatile bool _writeStop;
    private int _writePending = -1;

    // Periodic VCP brightness poller (syncs monitor OSD physical button changes)
    private readonly Thread _vcpThread;
    private volatile bool _vcpStop;
    private long _vcpQuietUntilTick;

    // Reinforce SDR hardware brightness after HDR -> SDR transitions
    private int _reinforceGeneration;

    private bool _updatingUi;
    private bool _optionsOpen;
    private bool _windowVisible;
    private bool _allowClose;
    private bool _hdrOn;
    private bool _hdrSupported = true;
    private int _currentPercent = 50;
    private string _warningText = string.Empty;
    private List<MonitorEntry> _monitors = [];

    public MainWindow(bool startMinimizedArg)
    {
        InitializeComponent();

        _startMinimizedArg = startMinimizedArg;
        _cfg = AppConfig.Load();
        MonitorCatalog.SelectedDevice = _cfg.MonitorDevice;

        // Sync startup checkbox with real Registry state
        _cfg.StartWithWindows = StartupHelper.IsEnabled();

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragArea);

        ConfigureWindowChrome();
        LoadWindowIcon();

        var iconPath = AppPaths.ResolveAssetPath("hdr2sdr.ico");
        _host = new WindowMessageHost(iconPath);
        _host.HotkeyPressed += OnHotkeyPressed;
        _host.ShowWindowRequested += () => DispatcherQueue.TryEnqueue(RestoreFromTray);
        _host.SyncStateRequested += () => DispatcherQueue.TryEnqueue(() => RefreshStatusAsync(syncSlider: true));
        _host.SystemTopologyOrSessionChanged += OnSystemTopologyOrSessionChanged;
        _host.TrayToggleHdrRequested += () => DispatcherQueue.TryEnqueue(DoToggleHdr);
        _host.TrayProfileRequested += idx => DispatcherQueue.TryEnqueue(() => ApplyProfileAsync(idx));
        _host.QuitRequested += () => DispatcherQueue.TryEnqueue(QuitApp);

        _appWindow.Closing += AppWindow_Closing;
        _appWindow.Changed += AppWindow_Changed;

        FooterVersionText.Text = BuildInfo.ShortVersion;

        ApplyTheme();
        UpdateProfileLabels();
        PopulateOptionsUi();
        RegisterHotkeys();
        ScheduleStartupHotkeyAssert();

        _writeThread = new Thread(BrightnessWriterLoop) { IsBackground = true, Name = "BrightnessWriter" };
        _writeThread.Start();

        _vcpThread = new Thread(VcpPollerLoop) { IsBackground = true, Name = "VcpPoller" };
        _vcpThread.Start();
    }

    public void InitializeAndMaybeShow()
    {
        bool startHidden = _startMinimizedArg || (_cfg.MinimizeToTray && _cfg.StartMinimized);
        if (startHidden)
        {
            _windowVisible = false;
            _host.SetTrayVisible(true);
            _appWindow.Hide();
        }
        else
        {
            _windowVisible = true;
            _host.SetTrayVisible(false);
            Activate();
            CenterOnPrimaryDisplay();
        }

        RefreshStatusAsync(syncSlider: true);
    }

    private void ConfigureWindowChrome()
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
        }

        double scale = GetDpiScale();
        _appWindow.Resize(new SizeInt32(
            (int)Math.Round(WindowWidthDips * scale),
            (int)Math.Round(WindowHeightDips * scale)));

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var tb = _appWindow.TitleBar;
            tb.ButtonBackgroundColor = Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Colors.Transparent;

            if (tb.RightInset > 0)
            {
                double rightDips = tb.RightInset / scale;
                CaptionButtonsSpacerColumn.Width = new GridLength(Math.Max(92, Math.Ceiling(rightDips)));
            }
        }
    }

    private void LoadWindowIcon()
    {
        var iconPath = AppPaths.ResolveAssetPath("hdr2sdr.ico");
        if (!File.Exists(iconPath))
            return;

        try
        {
            _appWindow.SetIcon(iconPath);
            TitleBarIcon.Source = new BitmapImage(new Uri(iconPath));
        }
        catch
        {
            // Ignore icon load failure
        }
    }

    private void CenterOnPrimaryDisplay()
    {
        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea is null)
            return;

        var work = displayArea.WorkArea;
        var size = _appWindow.Size;
        int x = work.X + (work.Width - size.Width) / 2;
        int y = work.Y + (work.Height - size.Height) / 2;
        _appWindow.Move(new PointInt32(Math.Max(work.X, x), Math.Max(work.Y, y)));
    }

    private double GetDpiScale()
    {
        uint dpi = GetDpiForWindow(_hwnd);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    private void TitleBarGrid_Loaded(object sender, RoutedEventArgs e) => UpdateTitleBarPassthroughRegions();
    private void TitleBarGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTitleBarPassthroughRegions();

    private void UpdateTitleBarPassthroughRegions()
    {
        if (TitleBarActionsPanel.ActualWidth <= 0 || TitleBarActionsPanel.ActualHeight <= 0)
            return;

        try
        {
            double scale = RootGrid.XamlRoot?.RasterizationScale ?? GetDpiScale();
            var transform = TitleBarActionsPanel.TransformToVisual(null);
            var bounds = transform.TransformBounds(new Windows.Foundation.Rect(
                0, 0, TitleBarActionsPanel.ActualWidth, TitleBarActionsPanel.ActualHeight));

            var rect = new RectInt32(
                (int)Math.Floor(bounds.X * scale),
                (int)Math.Floor(bounds.Y * scale),
                (int)Math.Ceiling(bounds.Width * scale),
                (int)Math.Ceiling(bounds.Height * scale));

            var nonClient = InputNonClientPointerSource.GetForWindowId(_appWindow.Id);
            nonClient.SetRegionRects(NonClientRegionKind.Passthrough, [rect]);
        }
        catch
        {
            // Ignore if non-client source is unavailable
        }
    }

    // -------------------------------------------------------------------------
    // Theme & dynamic Fluent card styling (Light + Dark)
    // -------------------------------------------------------------------------
    private void ApplyTheme()
    {
        bool light = _cfg.IsLightTheme;
        RootGrid.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
        ThemeIcon.Glyph = light ? "\uE708" : "\uE706"; // Moon in light mode, Sun in dark mode

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var tb = _appWindow.TitleBar;
            if (light)
            {
                tb.ButtonForegroundColor = Color.FromArgb(255, 24, 24, 27);
                tb.ButtonHoverForegroundColor = Color.FromArgb(255, 0, 0, 0);
                tb.ButtonHoverBackgroundColor = Color.FromArgb(24, 0, 0, 0);
                tb.ButtonPressedBackgroundColor = Color.FromArgb(40, 0, 0, 0);
                tb.ButtonInactiveForegroundColor = Color.FromArgb(140, 24, 24, 27);
            }
            else
            {
                tb.ButtonForegroundColor = Color.FromArgb(255, 244, 244, 246);
                tb.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
                tb.ButtonHoverBackgroundColor = Color.FromArgb(28, 255, 255, 255);
                tb.ButtonPressedBackgroundColor = Color.FromArgb(48, 255, 255, 255);
                tb.ButtonInactiveForegroundColor = Color.FromArgb(140, 244, 244, 246);
            }
        }

        UpdateOptionsButtonStyle();
        UpdateDynamicVisuals();
    }

    private void UpdateOptionsButtonStyle()
    {
        bool light = _cfg.IsLightTheme;
        if (_optionsOpen)
        {
            OptionsButton.Background = new SolidColorBrush(light
                ? Color.FromArgb(30, 0, 103, 192)
                : Color.FromArgb(46, 96, 205, 255));
            OptionsButton.BorderBrush = new SolidColorBrush(light
                ? Color.FromArgb(110, 0, 103, 192)
                : Color.FromArgb(130, 96, 205, 255));
        }
        else
        {
            OptionsButton.Background = new SolidColorBrush(Colors.Transparent);
            OptionsButton.BorderBrush = new SolidColorBrush(Colors.Transparent);
        }
    }

    private void UpdateDynamicVisuals()
    {
        bool light = _cfg.IsLightTheme;

        // 1. Status badge (HDR ON warm amber vs SDR cool blue)
        if (_hdrOn)
        {
            StatusBadge.Background = new SolidColorBrush(light
                ? Color.FromArgb(255, 254, 243, 199)
                : Color.FromArgb(255, 49, 36, 16));
            StatusBadge.BorderBrush = new SolidColorBrush(light
                ? Color.FromArgb(255, 217, 119, 6)
                : Color.FromArgb(190, 245, 158, 11));
            var fg = new SolidColorBrush(light
                ? Color.FromArgb(255, 146, 64, 14)
                : Color.FromArgb(255, 252, 211, 77));
            StatusBadgeDot.Fill = fg;
            StatusBadgeText.Foreground = fg;
            StatusBadgeText.Text = "HDR ON";
        }
        else
        {
            StatusBadge.Background = new SolidColorBrush(light
                ? Color.FromArgb(255, 224, 242, 254)
                : Color.FromArgb(255, 18, 37, 56));
            StatusBadge.BorderBrush = new SolidColorBrush(light
                ? Color.FromArgb(255, 2, 132, 199)
                : Color.FromArgb(180, 56, 189, 248));
            var fg = new SolidColorBrush(light
                ? Color.FromArgb(255, 3, 105, 161)
                : Color.FromArgb(255, 125, 211, 252));
            StatusBadgeDot.Fill = fg;
            StatusBadgeText.Foreground = fg;
            StatusBadgeText.Text = "SDR";
        }

        // 2. Profile cards active vs inactive styling
        bool jourActive = !_hdrOn;
        bool soirActive = _hdrOn && Math.Abs(_currentPercent - _cfg.ProfileSoirSdrPercent) <= 2;
        bool jeuActive = _hdrOn && !soirActive && Math.Abs(_currentPercent - _cfg.ProfileJeuSdrPercent) <= 2;

        StyleProfileCard(ProfileJourButton, ProfileJourIconBg, ProfileJourIcon, jourActive, isHdrProfile: false, light);
        StyleProfileCard(ProfileSoirButton, ProfileSoirIconBg, ProfileSoirIcon, soirActive, isHdrProfile: true, light);
        StyleProfileCard(ProfileJeuButton, ProfileJeuIconBg, ProfileJeuIcon, jeuActive, isHdrProfile: true, light);
    }

    private static void StyleProfileCard(
        Button card,
        Border iconBg,
        FontIcon icon,
        bool active,
        bool isHdrProfile,
        bool light)
    {
        if (active)
        {
            if (isHdrProfile)
            {
                card.Background = new SolidColorBrush(light
                    ? Color.FromArgb(235, 255, 248, 232)
                    : Color.FromArgb(220, 48, 37, 22));
                card.BorderBrush = new SolidColorBrush(light
                    ? Color.FromArgb(255, 217, 119, 6)
                    : Color.FromArgb(230, 245, 158, 11));
                iconBg.Background = new SolidColorBrush(light
                    ? Color.FromArgb(255, 254, 236, 179)
                    : Color.FromArgb(255, 76, 54, 22));
                icon.Foreground = new SolidColorBrush(light
                    ? Color.FromArgb(255, 180, 83, 9)
                    : Color.FromArgb(255, 252, 211, 77));
            }
            else
            {
                card.Background = new SolidColorBrush(light
                    ? Color.FromArgb(240, 234, 245, 255)
                    : Color.FromArgb(220, 20, 40, 60));
                card.BorderBrush = new SolidColorBrush(light
                    ? Color.FromArgb(255, 0, 103, 192)
                    : Color.FromArgb(230, 96, 205, 255));
                iconBg.Background = new SolidColorBrush(light
                    ? Color.FromArgb(255, 206, 233, 255)
                    : Color.FromArgb(255, 26, 58, 86));
                icon.Foreground = new SolidColorBrush(light
                    ? Color.FromArgb(255, 0, 95, 184)
                    : Color.FromArgb(255, 125, 211, 252));
            }
        }
        else
        {
            card.Background = new SolidColorBrush(light
                ? Color.FromArgb(185, 255, 255, 255)
                : Color.FromArgb(150, 42, 42, 46));
            card.BorderBrush = new SolidColorBrush(light
                ? Color.FromArgb(32, 0, 0, 0)
                : Color.FromArgb(32, 255, 255, 255));
            iconBg.Background = new SolidColorBrush(light
                ? Color.FromArgb(18, 0, 0, 0)
                : Color.FromArgb(22, 255, 255, 255));
            icon.Foreground = new SolidColorBrush(light
                ? Color.FromArgb(210, 45, 45, 50)
                : Color.FromArgb(225, 228, 228, 232));
        }
    }

    private void UpdateProfileLabels()
    {
        ProfileJourDesc.Text = "SDR natif";
        ProfileSoirDesc.Text = $"HDR • {_cfg.ProfileSoirSdrPercent} %";
        ProfileJeuDesc.Text = $"HDR • {_cfg.ProfileJeuSdrPercent} %";
        StepDownText.Text = $"Moins (−{_cfg.SdrStepPercent} %)";
        StepUpText.Text = $"Plus (+{_cfg.SdrStepPercent} %)";
    }

    // -------------------------------------------------------------------------
    // Status refresh & UI binding
    // -------------------------------------------------------------------------
    private void RefreshStatusAsync(bool syncSlider)
    {
        Task.Run(() =>
        {
            var target = DisplayControl.GetPrimaryHdrTarget();
            var resolved = MonitorCatalog.Resolve();
            bool hdrSupported = target?.HdrSupported ?? false;
            bool hdrOn = target?.HdrEnabled ?? false;
            int percent;
            string warning = string.Empty;

            if (target is null)
            {
                percent = 0;
                warning = "Aucun écran détecté";
            }
            else if (hdrOn)
            {
                percent = DisplayControl.NitsToPercent(target.SdrNits);
            }
            else
            {
                if (DisplayControl.TryGetBrightnessPercentFresh(out var fresh))
                {
                    percent = fresh;
                }
                else
                {
                    percent = DisplayControl.GetBrightnessPercentCached();
                    warning = "DDC/CI indisponible sur cet écran";
                }
            }

            string chip = resolved is not null
                ? resolved.Label.Replace(" · principal", string.Empty, StringComparison.Ordinal)
                : (target?.DeviceName.Replace(@"\\.\", string.Empty, StringComparison.Ordinal) ?? "Aucun écran");

            DispatcherQueue.TryEnqueue(() =>
            {
                _hdrSupported = hdrSupported;
                _hdrOn = hdrOn;
                _warningText = warning;
                MonitorChipText.Text = chip;
                ApplyStateToUi(percent, syncSlider);
            });
        });
    }

    private void ApplyStateToUi(int percent, bool syncSlider)
    {
        _currentPercent = Math.Clamp(percent, 0, 100);
        _updatingUi = true;
        try
        {
            HdrToggleSwitch.IsEnabled = _hdrSupported;
            HdrToggleSwitch.IsOn = _hdrOn;
            ToolTipService.SetToolTip(HdrToggleSwitch, _hdrSupported
                ? "Activer ou désactiver le mode HDR Windows"
                : "HDR non supporté sur cet écran");

            if (syncSlider)
                BrightnessSlider.Value = _currentPercent;

            BigPercentText.Text = _currentPercent.ToString();
            SliderValueText.Text = $"{_currentPercent} %";

            if (_hdrOn)
            {
                int nits = DisplayControl.PercentToNits(_currentPercent);
                ModeTitleText.Text = "HDR activé";
                ModeDescText.Text = "Curseur Windows «\u00A0contenu SDR\u00A0»";
                BrightnessCardTitle.Text = "Luminosité du contenu SDR";
                NitsChipText.Text = $"• {nits} nits";
            }
            else
            {
                ModeTitleText.Text = "SDR natif";
                ModeDescText.Text = "Luminosité matérielle via DDC/CI";
                BrightnessCardTitle.Text = "Luminosité";
                NitsChipText.Text = string.Empty;
            }

            string baseStatus = _hdrOn
                ? $"HDR ON  •  SDR {_currentPercent} %"
                : $"SDR  •  luminosité {_currentPercent} %";

            FooterStatusText.Text = string.IsNullOrEmpty(_warningText)
                ? baseStatus
                : $"{baseStatus}  •  ⚠ {_warningText}";

            string trayTip = _hdrOn
                ? $"Hdr2sdr — HDR ON | SDR {_currentPercent} %"
                : $"Hdr2sdr — SDR | luminosité {_currentPercent} %";
            _host.UpdateTrayState(trayTip, _hdrOn, _cfg.ProfileSoirSdrPercent, _cfg.ProfileJeuSdrPercent);

            UpdateDynamicVisuals();
        }
        finally
        {
            _updatingUi = false;
        }
    }

    // -------------------------------------------------------------------------
    // Brightness writer & VCP background poller
    // -------------------------------------------------------------------------
    private void RequestBrightnessWrite(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        _vcpQuietUntilTick = Environment.TickCount64 + 2500;
        lock (_writeSync)
        {
            _writePending = percent;
        }
        _writeEvent.Set();
    }

    private void CancelPendingBrightness()
    {
        lock (_writeSync)
        {
            _writePending = -1;
        }
    }

    private void BrightnessWriterLoop()
    {
        while (!_writeStop)
        {
            _writeEvent.WaitOne(250);
            if (_writeStop)
                break;

            int value;
            lock (_writeSync)
            {
                value = _writePending;
                _writePending = -1;
            }

            if (value < 0)
                continue;

            MonitorBrightness.BeginHold();
            try
            {
                while (value >= 0 && !_writeStop)
                {
                    bool ok = DisplayControl.SetBrightnessPercent(value);
                    string warn = ok ? string.Empty : DisplayControl.LastError;

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_warningText != warn)
                        {
                            _warningText = warn;
                            ApplyStateToUi(_currentPercent, syncSlider: false);
                        }
                    });

                    lock (_writeSync)
                    {
                        value = _writePending;
                        _writePending = -1;
                    }
                }
            }
            finally
            {
                MonitorBrightness.EndHold();
            }
        }
    }

    private void VcpPollerLoop()
    {
        while (!_vcpStop)
        {
            for (int i = 0; i < 25 && !_vcpStop; i++)
                Thread.Sleep(100);

            if (_vcpStop || !_windowVisible || _hdrOn)
                continue;

            if (Environment.TickCount64 < _vcpQuietUntilTick)
                continue;

            lock (_writeSync)
            {
                if (_writePending >= 0)
                    continue;
            }

            if (MonitorBrightness.TryReadVcpPercent(out int vcp))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_hdrOn && vcp != _currentPercent && Environment.TickCount64 >= _vcpQuietUntilTick)
                    {
                        _warningText = string.Empty;
                        ApplyStateToUi(vcp, syncSlider: true);
                    }
                });
            }
        }
    }

    private void ScheduleBrightnessReinforce(int percent)
    {
        if (percent < 0)
            return;

        int gen = Interlocked.Increment(ref _reinforceGeneration);
        Task.Run(async () =>
        {
            int[] delays = [400, 700, 900];
            foreach (int delay in delays)
            {
                await Task.Delay(delay);
                if (gen != _reinforceGeneration || _writeStop || DisplayControl.IsHdrOn())
                    return;

                MonitorBrightness.ForceSetPercent(percent);
            }
        });
    }

    // -------------------------------------------------------------------------
    // HDR & Profile actions
    // -------------------------------------------------------------------------
    private void DoSetHdr(bool wantOn)
    {
        CancelPendingBrightness();
        _vcpQuietUntilTick = Environment.TickCount64 + 3500;
        FooterStatusText.Text = wantOn ? "Activation du HDR…" : "Passage en SDR natif…";

        Task.Run(() =>
        {
            var target = DisplayControl.GetPrimaryHdrTarget();
            if (target is null)
            {
                DispatcherQueue.TryEnqueue(() => RefreshStatusAsync(syncSlider: true));
                return;
            }

            if (wantOn)
            {
                DisplayControl.RememberSdrBrightness();
                DisplayControl.SetHdr(target, enabled: true);
            }
            else
            {
                DisplayControl.LeaveHdrRestoringBrightness(out int restored);
                if (restored >= 0)
                    ScheduleBrightnessReinforce(restored);
            }

            DispatcherQueue.TryEnqueue(() => RefreshStatusAsync(syncSlider: true));
        });
    }

    private void DoToggleHdr()
    {
        bool targetState = !DisplayControl.IsHdrOn();
        DoSetHdr(targetState);
    }

    private void ApplyProfileAsync(int which)
    {
        CancelPendingBrightness();
        _vcpQuietUntilTick = Environment.TickCount64 + 3500;

        FooterStatusText.Text = which switch
        {
            0 => "Application du profil Jour…",
            1 => "Application du profil Soir…",
            _ => "Application du profil Jeu…"
        };

        Task.Run(() =>
        {
            bool ok;
            string msg;
            int reinforce = -1;

            switch (which)
            {
                case 0:
                    ok = Profiles.ApplyJour(out msg);
                    reinforce = DisplayControl.GetRememberedSdrBrightness();
                    break;
                case 1:
                    ok = Profiles.ApplySoir(_cfg, out msg);
                    break;
                default:
                    ok = Profiles.ApplyJeu(_cfg, out msg);
                    break;
            }

            if (ok && which == 0 && reinforce >= 0)
                ScheduleBrightnessReinforce(reinforce);

            DispatcherQueue.TryEnqueue(() =>
            {
                _warningText = ok ? string.Empty : msg.Replace("ERROR: ", string.Empty, StringComparison.Ordinal);
                RefreshStatusAsync(syncSlider: true);
            });
        });
    }

    private void NudgeBrightness(int delta)
    {
        int next = Math.Clamp(_currentPercent + delta, 0, 100);
        ApplyStateToUi(next, syncSlider: true);
        RequestBrightnessWrite(next);
    }

    // -------------------------------------------------------------------------
    // Home View Event Handlers
    // -------------------------------------------------------------------------
    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _cfg.Theme = _cfg.IsLightTheme ? "dark" : "light";
        _cfg.Save();
        ApplyTheme();
    }

    private void OptionsButton_Click(object sender, RoutedEventArgs e)
    {
        SetOptionsOpen(!_optionsOpen);
    }

    private void BackFromOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        SetOptionsOpen(false);
    }

    private void SetOptionsOpen(bool open)
    {
        _optionsOpen = open;
        HomeView.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        OptionsView.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        UpdateOptionsButtonStyle();

        if (open)
            PopulateOptionsUi();
    }

    private void HdrToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingUi)
            return;

        DoSetHdr(HdrToggleSwitch.IsOn);
    }

    private void BrightnessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingUi)
            return;

        int val = Math.Clamp((int)Math.Round(e.NewValue), 0, 100);
        if (val == _currentPercent)
            return;

        ApplyStateToUi(val, syncSlider: false);
        RequestBrightnessWrite(val);
    }

    private void BrightnessSlider_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(BrightnessSlider).Properties.MouseWheelDelta;
        if (delta == 0)
            return;

        NudgeBrightness(delta > 0 ? _cfg.SdrStepPercent : -_cfg.SdrStepPercent);
        e.Handled = true;
    }

    private void StepDownButton_Click(object sender, RoutedEventArgs e) => NudgeBrightness(-_cfg.SdrStepPercent);
    private void StepUpButton_Click(object sender, RoutedEventArgs e) => NudgeBrightness(_cfg.SdrStepPercent);

    private void ProfileJourButton_Click(object sender, RoutedEventArgs e) => ApplyProfileAsync(0);
    private void ProfileSoirButton_Click(object sender, RoutedEventArgs e) => ApplyProfileAsync(1);
    private void ProfileJeuButton_Click(object sender, RoutedEventArgs e) => ApplyProfileAsync(2);

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && _optionsOpen)
        {
            SetOptionsOpen(false);
            e.Handled = true;
        }
    }

    // -------------------------------------------------------------------------
    // Options View Event Handlers
    // -------------------------------------------------------------------------
    private void PopulateOptionsUi()
    {
        _updatingUi = true;
        try
        {
            FillMonitorsCombo();

            StepPercentBox.Value = _cfg.SdrStepPercent;
            ProfileSoirBox.Value = _cfg.ProfileSoirSdrPercent;
            ProfileJeuBox.Value = _cfg.ProfileJeuSdrPercent;

            MinimizeToTraySwitch.IsOn = _cfg.MinimizeToTray;
            StartMinimizedSwitch.IsOn = _cfg.StartMinimized;
            StartMinimizedSwitch.IsEnabled = _cfg.MinimizeToTray;
            StartWithWindowsSwitch.IsOn = _cfg.StartWithWindows;

            HotkeysEnabledSwitch.IsOn = _cfg.HotkeysEnabled;
            HotkeyToggleBox.Text = _cfg.HotkeyToggleHdr;
            HotkeyUpBox.Text = _cfg.HotkeySdrUp;
            HotkeyDownBox.Text = _cfg.HotkeySdrDown;
            HotkeyJourBox.Text = _cfg.HotkeyProfileJour;
            HotkeySoirBox.Text = _cfg.HotkeyProfileSoir;
            HotkeyJeuBox.Text = _cfg.HotkeyProfileJeu;
            UpdateHotkeyInputsEnabled();

            ConfigModeTitle.Text = AppPaths.IsPortable ? "Configuration (mode portable)" : "Configuration (dossier utilisateur)";
            ConfigPathText.Text = AppPaths.ConfigPath;
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void FillMonitorsCombo()
    {
        _monitors = MonitorCatalog.List();
        MonitorComboBox.Items.Clear();

        if (_monitors.Count == 0)
        {
            MonitorComboBox.Items.Add("Aucun écran détecté");
            MonitorComboBox.SelectedIndex = 0;
            MonitorComboBox.IsEnabled = false;
            return;
        }

        MonitorComboBox.IsEnabled = true;
        int selectedIdx = 0;
        for (int i = 0; i < _monitors.Count; i++)
        {
            MonitorComboBox.Items.Add(_monitors[i].Label);
            if (!string.IsNullOrEmpty(_cfg.MonitorDevice)
                && string.Equals(_monitors[i].DeviceName, _cfg.MonitorDevice, StringComparison.OrdinalIgnoreCase))
            {
                selectedIdx = i;
            }
            else if (string.IsNullOrEmpty(_cfg.MonitorDevice) && _monitors[i].Primary)
            {
                selectedIdx = i;
            }
        }

        MonitorComboBox.SelectedIndex = selectedIdx;
    }

    private void UpdateHotkeyInputsEnabled()
    {
        bool enabled = _cfg.HotkeysEnabled;
        HotkeysFieldsPanel.Opacity = enabled ? 1.0 : 0.48;
        HotkeyToggleBox.IsEnabled = enabled;
        HotkeyUpBox.IsEnabled = enabled;
        HotkeyDownBox.IsEnabled = enabled;
        HotkeyJourBox.IsEnabled = enabled;
        HotkeySoirBox.IsEnabled = enabled;
        HotkeyJeuBox.IsEnabled = enabled;
    }

    private void RefreshMonitorsButton_Click(object sender, RoutedEventArgs e)
    {
        _updatingUi = true;
        try
        {
            FillMonitorsCombo();
        }
        finally
        {
            _updatingUi = false;
        }
        RefreshStatusAsync(syncSlider: true);
    }

    private void MonitorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || MonitorComboBox.SelectedIndex < 0 || MonitorComboBox.SelectedIndex >= _monitors.Count)
            return;

        _cfg.MonitorDevice = _monitors[MonitorComboBox.SelectedIndex].DeviceName;
        MonitorCatalog.SelectedDevice = _cfg.MonitorDevice;
        MonitorBrightness.Invalidate();
        _cfg.Save();
        RefreshStatusAsync(syncSlider: true);
    }

    private void NumericOption_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_updatingUi || double.IsNaN(args.NewValue))
            return;

        _cfg.SdrStepPercent = Math.Clamp((int)Math.Round(double.IsNaN(StepPercentBox.Value) ? 5 : StepPercentBox.Value), 1, 25);
        _cfg.ProfileSoirSdrPercent = Math.Clamp((int)Math.Round(double.IsNaN(ProfileSoirBox.Value) ? 0 : ProfileSoirBox.Value), 0, 100);
        _cfg.ProfileJeuSdrPercent = Math.Clamp((int)Math.Round(double.IsNaN(ProfileJeuBox.Value) ? 80 : ProfileJeuBox.Value), 0, 100);
        _cfg.Save();

        UpdateProfileLabels();
        UpdateDynamicVisuals();
    }

    private void GeneralOption_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingUi)
            return;

        _cfg.MinimizeToTray = MinimizeToTraySwitch.IsOn;
        StartMinimizedSwitch.IsEnabled = _cfg.MinimizeToTray;
        if (!_cfg.MinimizeToTray && StartMinimizedSwitch.IsOn)
        {
            _updatingUi = true;
            StartMinimizedSwitch.IsOn = false;
            _updatingUi = false;
        }
        _cfg.StartMinimized = StartMinimizedSwitch.IsOn;

        if (_cfg.StartWithWindows != StartWithWindowsSwitch.IsOn)
        {
            _cfg.StartWithWindows = StartWithWindowsSwitch.IsOn;
            StartupHelper.SetEnabled(_cfg.StartWithWindows);
        }

        bool hotkeysChanged = _cfg.HotkeysEnabled != HotkeysEnabledSwitch.IsOn;
        _cfg.HotkeysEnabled = HotkeysEnabledSwitch.IsOn;
        UpdateHotkeyInputsEnabled();

        _cfg.Save();
        if (hotkeysChanged)
            RegisterHotkeys();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        e.Handled = true;

        if (e.Key == VirtualKey.Escape)
        {
            OptionsButton.Focus(FocusState.Programmatic);
            return;
        }

        if (e.Key is VirtualKey.Back or VirtualKey.Delete)
        {
            ApplyCapturedHotkey(box, string.Empty);
            return;
        }

        uint vk = (uint)e.Key;
        if (HotkeyParser.IsModifierVk(vk))
            return;

        bool ctrl = IsKeyDown(VirtualKey.Control);
        bool alt = IsKeyDown(VirtualKey.Menu);
        bool shift = IsKeyDown(VirtualKey.Shift);
        bool win = IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows);

        string chord = HotkeyParser.Format(ctrl, alt, shift, win, vk);
        if (!string.IsNullOrEmpty(chord))
            ApplyCapturedHotkey(box, chord);
    }

    private void ApplyCapturedHotkey(TextBox box, string chord)
    {
        box.Text = chord;
        switch (box.Tag as string)
        {
            case "toggle": _cfg.HotkeyToggleHdr = chord; break;
            case "up": _cfg.HotkeySdrUp = chord; break;
            case "down": _cfg.HotkeySdrDown = chord; break;
            case "jour": _cfg.HotkeyProfileJour = chord; break;
            case "soir": _cfg.HotkeyProfileSoir = chord; break;
            case "jeu": _cfg.HotkeyProfileJeu = chord; break;
        }

        _cfg.Save();
        RegisterHotkeys();
    }

    private void ResetHotkeysButton_Click(object sender, RoutedEventArgs e)
    {
        _cfg.ResetHotkeysToDefaults();
        _cfg.Save();
        PopulateOptionsUi();
        RegisterHotkeys();
    }

    private void OpenConfigFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.DataDir) { UseShellExecute = true });
        }
        catch
        {
            // Ignore shell launch errors
        }
    }

    private static bool IsKeyDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

    // -------------------------------------------------------------------------
    // Global Hotkeys
    // -------------------------------------------------------------------------
    private void RegisterHotkeys()
    {
        _host.UnregisterAllHotkeys();
        if (!_cfg.HotkeysEnabled)
            return;

        TryRegister(WindowMessageHost.HotkeyToggleHdr, _cfg.HotkeyToggleHdr, allowRepeat: false);
        TryRegister(WindowMessageHost.HotkeySdrUp, _cfg.HotkeySdrUp, allowRepeat: true);
        TryRegister(WindowMessageHost.HotkeySdrDown, _cfg.HotkeySdrDown, allowRepeat: true);
        TryRegister(WindowMessageHost.HotkeyProfileJour, _cfg.HotkeyProfileJour, allowRepeat: false);
        TryRegister(WindowMessageHost.HotkeyProfileSoir, _cfg.HotkeyProfileSoir, allowRepeat: false);
        TryRegister(WindowMessageHost.HotkeyProfileJeu, _cfg.HotkeyProfileJeu, allowRepeat: false);
    }

    private void TryRegister(int id, string chord, bool allowRepeat)
    {
        if (string.IsNullOrWhiteSpace(chord))
            return;

        if (!_host.RegisterHotkey(id, chord, allowRepeat, out int err))
        {
            LogHotkey($"id={id} '{chord}' -> FAIL err={err}");
        }
    }

    private void ScheduleStartupHotkeyAssert()
    {
        Task.Run(async () =>
        {
            await Task.Delay(3000);
            if (!_writeStop)
                DispatcherQueue.TryEnqueue(RegisterHotkeys);
        });
    }

    private void OnSystemTopologyOrSessionChanged()
    {
        MonitorBrightness.Invalidate();
        DispatcherQueue.TryEnqueue(() =>
        {
            RegisterHotkeys();
            if (_optionsOpen)
                PopulateOptionsUi();
            RefreshStatusAsync(syncSlider: true);
        });
    }

    private void OnHotkeyPressed(int id)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (id)
            {
                case WindowMessageHost.HotkeyToggleHdr:
                    DoToggleHdr();
                    break;
                case WindowMessageHost.HotkeySdrUp:
                    NudgeBrightness(_cfg.SdrStepPercent);
                    break;
                case WindowMessageHost.HotkeySdrDown:
                    NudgeBrightness(-_cfg.SdrStepPercent);
                    break;
                case WindowMessageHost.HotkeyProfileJour:
                    ApplyProfileAsync(0);
                    break;
                case WindowMessageHost.HotkeyProfileSoir:
                    ApplyProfileAsync(1);
                    break;
                case WindowMessageHost.HotkeyProfileJeu:
                    ApplyProfileAsync(2);
                    break;
            }
        });
    }

    private static void LogHotkey(string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.AppendAllText(AppPaths.HotkeyLogPath, $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // Ignore log errors
        }
    }

    // -------------------------------------------------------------------------
    // Window Lifecycle & Tray Integration
    // -------------------------------------------------------------------------
    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange
            && _cfg.MinimizeToTray
            && sender.Presenter is OverlappedPresenter p
            && p.State == OverlappedPresenterState.Minimized)
        {
            HideToTray();
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_allowClose && _cfg.MinimizeToTray)
        {
            args.Cancel = true;
            HideToTray();
            return;
        }

        ShutdownBackgroundWorkers();
    }

    private void HideToTray()
    {
        _windowVisible = false;
        _host.SetTrayVisible(true);
        _appWindow.Hide();
    }

    private void RestoreFromTray()
    {
        _windowVisible = true;
        _host.SetTrayVisible(false);
        _appWindow.Show();
        if (_appWindow.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Minimized)
            p.Restore();

        ShowWindow(_hwnd, SW_RESTORE);
        SetForegroundWindow(_hwnd);
        Activate();
        RefreshStatusAsync(syncSlider: true);
    }

    private void QuitApp()
    {
        _allowClose = true;
        ShutdownBackgroundWorkers();
        Close();
    }

    private void ShutdownBackgroundWorkers()
    {
        _writeStop = true;
        _vcpStop = true;
        _writeEvent.Set();
        _host.Dispose();
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
