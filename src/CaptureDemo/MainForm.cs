using CaptureDemo.Core;

namespace CaptureDemo;

internal sealed class MainForm : Form
{
    private const int HotkeySnip = 0xC0E0;
    private const int HotkeyElement = 0xC0E1;
    private const int HotkeyLong = 0xC0E2;

    private readonly LaunchMode _launch;
    private readonly TextBox _status;
    private readonly AppSettings _settings;
    private ToastForm? _toast;
    private string? _lastFile;
    private int _gate;
    private bool _snipHotkey;
    private bool _elementHotkey;
    private bool _longHotkey;
    private bool _autoStarted;
    private bool _openSettings;
    private bool _allowExit;
    private bool _showWhenDone;
    private NotifyIcon? _tray;
    private IntPtr _trayIconHandle;
    private IntPtr _hotkeyWindow;

    public MainForm(LaunchMode launch)
    {
        _launch = launch;
        _settings = AppSettings.Load(AppSettings.DefaultFilePath());
        Font = UiFonts.Body;
        AutoScaleMode = AutoScaleMode.Font;
        Text = "截图";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(420, 280);
        ClientSize = new Size(440, 300);
        BackColor = Theme.FluentBack;
        KeyPreview = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 24, 28, 16),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Theme.FluentBack,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(new Label
        {
            Text = "截图",
            Font = UiFonts.Title,
            ForeColor = Theme.Text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);

        var start = new Button
        {
            Text = "开始截图",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Accent,
            ForeColor = Color.White,
            Margin = new Padding(0, 4, 0, 4),
        };
        start.FlatAppearance.BorderSize = 0;
        start.Click += (_, _) => _ = StartSnipAsync();

        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Theme.FluentBack,
            Padding = new Padding(0, 6, 0, 0),
        };
        var settingsButton = new Button
        {
            Text = "设置",
            AutoSize = true,
            MinimumSize = new Size(88, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Theme.Text,
        };
        settingsButton.FlatAppearance.BorderColor = Theme.CardBorder;
        settingsButton.Click += (_, _) => EditSettings();
        var folderLink = new LinkLabel
        {
            Text = "打开保存目录",
            AutoSize = true,
            LinkColor = Theme.Accent,
            ActiveLinkColor = Theme.Accent,
            VisitedLinkColor = Theme.Accent,
            Margin = new Padding(16, 8, 0, 0),
        };
        folderLink.LinkClicked += (_, _) => OpenFolder();
        row.Controls.Add(settingsButton);
        row.Controls.Add(folderLink);

        _status = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.FluentBack,
            ForeColor = Theme.Muted,
            TabStop = false,
            Text = "",
        };

        root.Controls.Add(start, 0, 1);
        root.Controls.Add(row, 0, 2);
        root.Controls.Add(_status, 0, 3);
        Controls.Add(root);
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!IsHandleCreated)
        {
            CreateHandle();
            value = false;
        }

        base.SetVisibleCore(value);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ShowInTaskbar = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowAffinity.TryExcludeFromCapture(Handle);
        _hotkeyWindow = Handle;
        EnsureTray();
        RegisterHotkeys();
        if (_autoStarted)
        {
            return;
        }

        _autoStarted = true;
        BeginInvoke(StartFromLaunch);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotkeys();
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WmHotkey)
        {
            switch (m.WParam.ToInt32())
            {
                case HotkeySnip:
                    _ = StartSnipAsync();
                    break;
                case HotkeyElement:
                    _ = StartElementAsync();
                    break;
                case HotkeyLong:
                    _ = StartLongAsync();
                    break;
            }
        }

        base.WndProc(ref m);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Matches(e, _settings.AwakenHotkey))
        {
            e.Handled = true;
            _ = StartSnipAsync();
            return;
        }

        if (Matches(e, _settings.ElementHotkey))
        {
            e.Handled = true;
            _ = StartElementAsync();
            return;
        }

        if (Matches(e, _settings.LongHotkey))
        {
            e.Handled = true;
            _ = StartLongAsync();
            return;
        }

        base.OnKeyDown(e);
    }

    private async Task StartSnipAsync()
    {
        if (Interlocked.Exchange(ref _gate, 1) == 1)
        {
            return;
        }

        _showWhenDone = Visible;
        try
        {
            HideToTray();
            var result = await SnipSession.RunAsync(_settings, SnipMode.Rectangle);
            if (result.Follow == SnipFollow.Long)
            {
                Apply(await LongCaptureSession.RunAsync(_settings, result.Preset));
            }
            else if (result.Follow == SnipFollow.Element)
            {
                Apply(await ElementCaptureSession.RunAsync(_settings));
            }
            else if (result.Follow == SnipFollow.Settings)
            {
                _openSettings = true;
            }
            else
            {
                Apply(result.Outcome);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ReportFailure(ex);
        }
        finally
        {
            FinishCapture();
        }
    }

    private async Task StartElementAsync()
    {
        if (Interlocked.Exchange(ref _gate, 1) == 1)
        {
            return;
        }

        _showWhenDone = Visible;
        try
        {
            HideToTray();
            Apply(await ElementCaptureSession.RunAsync(_settings));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ReportFailure(ex);
        }
        finally
        {
            FinishCapture();
        }
    }

    private async Task StartLongAsync()
    {
        if (Interlocked.Exchange(ref _gate, 1) == 1)
        {
            return;
        }

        _showWhenDone = Visible;
        try
        {
            HideToTray();
            Apply(await LongCaptureSession.RunAsync(_settings));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ReportFailure(ex);
        }
        finally
        {
            FinishCapture();
        }
    }

    private void FinishCapture()
    {
        Interlocked.Exchange(ref _gate, 0);
        if (IsDisposed)
        {
            return;
        }

        if (_showWhenDone)
        {
            ShowFromTray();
        }

        if (_openSettings)
        {
            _openSettings = false;
            EditSettings();
        }
    }

    private void EditSettings()
    {
        if (!SettingsForm.TryEdit(this, _settings))
        {
            return;
        }

        try
        {
            _settings.Save(AppSettings.DefaultFilePath());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("设置没能写入：" + ex.Message);
            return;
        }

        RegisterHotkeys();
        SetStatus("设置已保存。");
    }

    private void RegisterHotkeys()
    {
        UnregisterHotkeys();
        if (_hotkeyWindow == IntPtr.Zero)
        {
            return;
        }

        _snipHotkey = TryRegister(HotkeySnip, _settings.AwakenHotkey);
        _elementHotkey = TryRegister(HotkeyElement, _settings.ElementHotkey);
        _longHotkey = TryRegister(HotkeyLong, _settings.LongHotkey);
        if (!_snipHotkey || !_elementHotkey || !_longHotkey)
        {
            SetStatus("有全局热键没有注册成功（可能被别的程序占用）。窗口在前面时仍可用同一组按键。");
        }
    }

    private bool TryRegister(int id, string text)
    {
        if (!HotkeyChord.TryParse(text, out var chord))
        {
            return false;
        }

        return NativeMethods.RegisterHotKey(_hotkeyWindow, id, chord.Modifiers | NativeMethods.ModNoRepeat, chord.VirtualKey);
    }

    private void UnregisterHotkeys()
    {
        if (_hotkeyWindow == IntPtr.Zero)
        {
            return;
        }

        if (_snipHotkey)
        {
            NativeMethods.UnregisterHotKey(_hotkeyWindow, HotkeySnip);
        }

        if (_elementHotkey)
        {
            NativeMethods.UnregisterHotKey(_hotkeyWindow, HotkeyElement);
        }

        if (_longHotkey)
        {
            NativeMethods.UnregisterHotKey(_hotkeyWindow, HotkeyLong);
        }

        _snipHotkey = false;
        _elementHotkey = false;
        _longHotkey = false;
    }

    private static bool Matches(KeyEventArgs e, string text)
    {
        if (!HotkeyChord.TryParse(text, out var chord) || (int)e.KeyCode != chord.VirtualKey)
        {
            return false;
        }

        var ctrl = (chord.Modifiers & HotkeyChord.ModControl) != 0;
        var alt = (chord.Modifiers & HotkeyChord.ModAlt) != 0;
        var shift = (chord.Modifiers & HotkeyChord.ModShift) != 0;
        return e.Control == ctrl && e.Alt == alt && e.Shift == shift;
    }

    private void Apply(CaptureOutcome outcome)
    {
        if (IsDisposed)
        {
            return;
        }

        _lastFile = outcome.FilePath;
        SetStatus(outcome.Message);
        if (outcome.Kind is CaptureOutcomeKind.Cancelled or CaptureOutcomeKind.OpenSettings)
        {
            return;
        }

        ShowToast(outcome.Message, outcome.FilePath);
    }

    private void ReportFailure(Exception ex)
    {
        if (IsDisposed)
        {
            return;
        }

        var message = "失败：" + ex.Message;
        SetStatus(message);
        ShowToast(message, null);
    }

    private void StartFromLaunch()
    {
        if (_launch == LaunchMode.Element)
        {
            _ = StartElementAsync();
            return;
        }

        if (_launch == LaunchMode.Long)
        {
            _ = StartLongAsync();
            return;
        }

        if (_launch == LaunchMode.Snip)
        {
            _ = StartSnipAsync();
            return;
        }

        if (!_snipHotkey || !_elementHotkey || !_longHotkey)
        {
            ShowFromTray();
            return;
        }

        HideToTray();
        _tray?.ShowBalloonTip(2800, "截图", "已在托盘。左键打开主窗口，右键可以选择截图或退出。", ToolTipIcon.None);
    }

    private void EnsureTray()
    {
        if (_tray != null)
        {
            return;
        }

        _trayIconHandle = CreateTrayIcon();
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开主窗口", null, (_, _) => ShowFromTray());
        menu.Items.Add("截图", null, (_, _) => _ = StartSnipAsync());
        menu.Items.Add("设置", null, (_, _) => EditSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp());
        _tray = new NotifyIcon
        {
            Icon = Icon.FromHandle(_trayIconHandle),
            Text = "截图",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowFromTray();
            }
        };
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void ShowFromTray()
    {
        if (IsDisposed)
        {
            return;
        }

        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _allowExit = true;
        UnregisterHotkeys();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        if (_trayIconHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_trayIconHandle);
            _trayIconHandle = IntPtr.Zero;
        }

        Application.Exit();
    }

    private static IntPtr CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(32, 32, 32));
            graphics.FillEllipse(brush, 1, 1, 30, 30);
            SnipIcons.Rect(graphics, new Rectangle(9, 9, 14, 14), Color.White);
        }

        return bitmap.GetHicon();
    }

    private void SetStatus(string message)
    {
        if (!IsDisposed)
        {
            _status.Text = message;
        }
    }

    private void ShowToast(string message, string? path)
    {
        if (IsDisposed)
        {
            return;
        }

        _toast?.Close();
        _toast = new ToastForm(message, path);
        _toast.Show();
    }

    private void OpenFolder()
    {
        try
        {
            CaptureSaver.Reveal(_lastFile, _settings);
        }
        catch (Exception ex)
        {
            SetStatus("无法打开目录：" + ex.Message);
        }
    }
}
