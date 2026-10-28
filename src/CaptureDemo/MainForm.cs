using CaptureDemo.Clips;
using CaptureDemo.Core;
using CaptureDemo.Native;
using CaptureDemo.Snip;
using CaptureDemo.Ui;

namespace CaptureDemo;

internal sealed class MainForm : ShellForm
{
    private const int HotkeySnip = 0xC0E0;
    private const int HotkeyElement = 0xC0E1;
    private const int HotkeyLong = 0xC0E2;
    private const int HotkeyClipboard = 0xC0E3;

    private readonly LaunchMode _launch;
    private readonly AppSettings _settings;
    private readonly ClipboardService _clipboard;
    private readonly bool _preview;

    private FlatButton? _start;
    private FlatButton? _long;
    private FlatButton? _element;
    private FlatButton? _clipboardButton;
    private FlatButton? _settingsButton;
    private FlatButton? _folderButton;
    private CardPanel? _captureCard;
    private CardPanel? _clipboardCard;
    private Label? _clipboardTitle;
    private Label? _clipboardDetail;
    private Label? _status;

    private NotifyIcon? _tray;
    private ToastForm? _toast;
    private string? _lastFile;
    private int _gate;
    private bool _snipHotkey;
    private bool _elementHotkey;
    private bool _longHotkey;
    private bool _clipboardHotkey;
    private bool _autoStarted;
    private bool _openSettings;
    private bool _allowExit;
    private bool _showWhenDone;
    private IntPtr _hotkeyWindow;

    public MainForm(LaunchMode launch, bool preview = false, AppSettings? settingsOverride = null)
    {
        _launch = launch;
        _preview = preview;
        _settings = settingsOverride ?? AppSettings.Load(AppSettings.DefaultFilePath());
        Theme.Apply(ParseAppearance(_settings.Appearance));
        BackColor = Theme.AppBack;
        ForeColor = Theme.Text;
        _clipboard = new ClipboardService(_settings, listen: !preview, store: null);
        _clipboard.Changed += UpdateClipboardCard;

        Text = "截图";
        Icon = IconFactory.AppIcon();
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        KeyPreview = true;
        ClientSize = new Size(S(480), S(402));
        BuildContent();
    }

    private static Appearance ParseAppearance(string value) => value switch
    {
        "Light" => Appearance.Light,
        "Dark" => Appearance.Dark,
        _ => Appearance.System,
    };

    protected override void SetVisibleCore(bool value)
    {
        if (_preview)
        {
            base.SetVisibleCore(value);
            return;
        }

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
        if (_preview)
        {
            _autoStarted = true;
            return;
        }

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
                    return;
                case HotkeyElement:
                    _ = StartElementAsync();
                    return;
                case HotkeyLong:
                    _ = StartLongAsync();
                    return;
                case HotkeyClipboard:
                    _clipboard.TogglePanel();
                    return;
            }
        }

        if (SingleInstance.Message != 0 && m.Msg == SingleInstance.Message)
        {
            var mode = (LaunchMode)m.WParam.ToInt32();
            ShowFromTray();
            if (mode != LaunchMode.Menu)
            {
                StartFromLaunch();
            }

            return;
        }

        base.WndProc(ref m);
    }

    // ── layout ──────────────────────────────────────────────────────────────
    private void BuildContent()
    {
        SuspendLayout();
        Controls.Clear();

        var pad = S(24);
        var width = S(480) - (pad * 2);

        _captureCard = new CardPanel { Radius = S(12) };
        _captureCard.SetBounds(pad, S(88), width, S(128));
        Controls.Add(_captureCard);

        _start = new FlatButton
        {
            Text = "开始截图",
            Variant = ButtonVariant.Primary,
            Glyph = SnipIcons.Rect,
            GlyphSize = S(20),
            CornerRadius = S(9),
            Font = UiFonts.ButtonTitle,
        };
        _start.SetBounds(S(14), S(14), width - S(28), S(46));
        _start.Clicked += (_, _) => _ = StartSnipAsync();
        _captureCard.Controls.Add(_start);

        var chipWidth = (width - S(28) - S(16)) / 3;
        _long = Chip("长截图", SnipIcons.Long, S(14), S(70), chipWidth);
        _long.Clicked += (_, _) => _ = StartLongAsync();
        _element = Chip("元素截图", SnipIcons.Element, S(14) + chipWidth + S(8), S(70), chipWidth);
        _element.Clicked += (_, _) => _ = StartElementAsync();
        _clipboardButton = Chip("剪贴板", SnipIcons.Clipboard, S(14) + ((chipWidth + S(8)) * 2), S(70), chipWidth);
        _clipboardButton.Clicked += (_, _) => _clipboard.TogglePanel();
        _captureCard.Controls.Add(_long);
        _captureCard.Controls.Add(_element);
        _captureCard.Controls.Add(_clipboardButton);

        _clipboardCard = new CardPanel { Radius = S(12) };
        _clipboardCard.SetBounds(pad, S(228), width, S(84));
        Controls.Add(_clipboardCard);

        _clipboardTitle = new Label
        {
            Text = "剪贴板",
            Font = UiFonts.BodyBold,
            ForeColor = Theme.Text,
            BackColor = Theme.Card,
            AutoSize = false,
        };
        _clipboardTitle.SetBounds(S(16), S(13), width - S(120), S(20));

        _clipboardDetail = new Label
        {
            Text = "",
            Font = UiFonts.Small,
            ForeColor = Theme.Muted,
            BackColor = Theme.Card,
            AutoEllipsis = true,
            AutoSize = false,
        };
        _clipboardDetail.SetBounds(S(16), S(37), width - S(120), S(20));

        var openClipboard = new FlatButton
        {
            Text = "打开",
            Variant = ButtonVariant.Ghost,
            Glyph = SnipIcons.Open,
            GlyphSize = S(15),
            CornerRadius = S(7),
        };
        openClipboard.SetBounds(width - S(96), S(24), S(80), S(34));
        openClipboard.Clicked += (_, _) => _clipboard.ShowPanel();
        _clipboardCard.Controls.Add(_clipboardTitle);
        _clipboardCard.Controls.Add(_clipboardDetail);
        _clipboardCard.Controls.Add(openClipboard);

        _status = new Label
        {
            Text = "",
            Font = UiFonts.Small,
            ForeColor = Theme.Muted,
            BackColor = Theme.AppBack,
            AutoEllipsis = true,
            AutoSize = false,
        };
        _status.SetBounds(pad, S(316), width, S(20));
        Controls.Add(_status);

        _settingsButton = new FlatButton
        {
            Text = "设置",
            Variant = ButtonVariant.Secondary,
            Glyph = SnipIcons.Sliders,
            GlyphSize = S(16),
            CornerRadius = S(8),
        };
        _settingsButton.SetBounds(pad, S(340), S(96), S(36));
        _settingsButton.Clicked += (_, _) => EditSettings();
        Controls.Add(_settingsButton);

        _folderButton = new FlatButton
        {
            Text = "打开保存目录",
            Variant = ButtonVariant.Secondary,
            Glyph = SnipIcons.Folder,
            GlyphSize = S(16),
            CornerRadius = S(8),
        };
        _folderButton.SetBounds(pad + S(104), S(340), S(152), S(36));        _folderButton.Clicked += (_, _) => OpenFolder();
        Controls.Add(_folderButton);

        ResumeLayout();
        UpdateClipboardCard();
        Invalidate();
    }

    private FlatButton Chip(string text, Action<Graphics, Rectangle, Color> glyph, int x, int y, int width)
    {
        var button = new FlatButton
        {
            Text = text,
            Variant = ButtonVariant.Ghost,
            Glyph = glyph,
            GlyphSize = S(17),
            GlyphGap = S(6),
            CornerRadius = S(8),
        };
        button.SetBounds(x, y, width, S(34));
        return button;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var pad = S(24);
        var tile = new Rectangle(pad, S(18), S(44), S(44));
        using (var path = UiKit.Rounded(tile, S(11)))
        using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                   tile,
                   Theme.IsDark ? Color.FromArgb(70, 74, 84) : Color.FromArgb(48, 52, 60),
                   Theme.IsDark ? Color.FromArgb(38, 40, 46) : Color.FromArgb(24, 26, 30),
                   System.Drawing.Drawing2D.LinearGradientMode.Vertical))
        {
            g.FillPath(brush, path);
        }

        SnipIcons.CaptureMark(g, tile, Color.FromArgb(246, 247, 250), 1.65f);

        TextRenderer.DrawText(
            g,
            "截图",
            UiFonts.Title,
            new Rectangle(tile.Right + S(12), S(16), S(240), S(28)),
            Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        TextRenderer.DrawText(
            g,
            _settings.AwakenHotkey + " 随时开始 · 关闭窗口后常驻托盘",
            UiFonts.Small,
            new Rectangle(tile.Right + S(12), S(44), S(360), S(20)),
            Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        base.OnPaint(e);
    }

    private void UpdateClipboardCard()
    {
        if (_clipboardDetail == null || _clipboardTitle == null || IsDisposed)
        {
            return;
        }

        var items = _clipboard.History.Ordered();
        _clipboardTitle.Text = items.Count == 0 ? "剪贴板" : $"剪贴板 · {items.Count} 条";
        _clipboardDetail.Text = items.Count == 0
            ? "复制点什么，这里就会出现最近记录"
            : items[0].KindLabel + " · " + items[0].Headline;
    }

    private void ApplyTheme()
    {
        Theme.Apply(ParseAppearance(_settings.Appearance));
        BackColor = Theme.AppBack;
        ForeColor = Theme.Text;
        BuildContent();
        SeedTray();
        if (IsHandleCreated)
        {
            ApplyChrome(Handle);
        }
    }

    // ── capture entry points ────────────────────────────────────────────────
    private async Task StartSnipAsync()
    {
        if (Interlocked.Exchange(ref _gate, 1) == 1)
        {
            return;
        }

        _showWhenDone = Visible;
        _clipboard.HidePanel();
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
        _clipboard.HidePanel();
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
        _clipboard.HidePanel();
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
        var appearanceBefore = _settings.Appearance;
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

        _clipboard.ApplySettings();
        StartupRegistration.Set(_settings.LaunchAtStartup);
        RegisterHotkeys();
        SetStatus("设置已保存。");
        if (!string.Equals(appearanceBefore, _settings.Appearance, StringComparison.Ordinal))
        {
            ApplyTheme();
        }
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
        _clipboardHotkey = TryRegister(HotkeyClipboard, _settings.ClipboardHotkey);
        if (!_snipHotkey || !_elementHotkey || !_longHotkey || !_clipboardHotkey)
        {
            SetStatus("有全局热键没有注册成功（可能被别的程序占用）。");
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

        if (_clipboardHotkey)
        {
            NativeMethods.UnregisterHotKey(_hotkeyWindow, HotkeyClipboard);
        }

        _snipHotkey = false;
        _elementHotkey = false;
        _longHotkey = false;
        _clipboardHotkey = false;
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

        if (_launch == LaunchMode.Clipboard)
        {
            HideToTray();
            _clipboard.ShowPanel();
            return;
        }

        if (!_snipHotkey || !_elementHotkey || !_longHotkey)
        {
            ShowFromTray();
            return;
        }

        HideToTray();
        _tray?.ShowBalloonTip(2600, "截图", "已在托盘。左键打开主窗口，右键可用剪贴板、截图或退出。", ToolTipIcon.None);
    }

    // ── tray ────────────────────────────────────────────────────────────────
    private void EnsureTray()
    {
        if (_tray != null)
        {
            return;
        }

        _tray = new NotifyIcon
        {
            Icon = IconFactory.TrayIcon(),
            Text = "截图",
            Visible = true,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowFromTray();
            }
        };
        SeedTray();
    }

    private void SeedTray()
    {
        if (_tray == null)
        {
            return;
        }

        var menu = BuildTrayMenu();
        var previous = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = menu;
        previous?.Dispose();
    }

    /// <summary>Shared with the dev preview so the tray menu can be reviewed as an image.</summary>
    internal ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new FluentMenuRenderer(),
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            Font = UiFonts.Body,
            ShowImageMargin = true,
            Padding = new Padding(4),
        };
        menu.Items.Add(TrayItem("打开主窗口", null, () => ShowFromTray(), bold: true));
        menu.Items.Add(TrayItem("截图", SnipIcons.Rect, () => _ = StartSnipAsync(), shortcut: _settings.AwakenHotkey));
        menu.Items.Add(TrayItem("长截图", SnipIcons.Long, () => _ = StartLongAsync(), shortcut: _settings.LongHotkey));
        menu.Items.Add(TrayItem("元素截图", SnipIcons.Element, () => _ = StartElementAsync(), shortcut: _settings.ElementHotkey));
        menu.Items.Add(TrayItem("剪贴板", SnipIcons.Clipboard, () => _clipboard.ShowPanel(), shortcut: _settings.ClipboardHotkey));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(TrayItem("设置", SnipIcons.Sliders, EditSettings));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(TrayItem("退出", null, ExitApp));
        return menu;
    }

    private ToolStripMenuItem TrayItem(
        string text,
        Action<Graphics, Rectangle, Color>? glyph,
        Action action,
        bool bold = false,
        string? shortcut = null)
    {
        var item = new ToolStripMenuItem(text)
        {
            ForeColor = Theme.Text,
            Font = bold ? UiFonts.BodyBold : UiFonts.Body,
            Padding = new Padding(4, 3, 4, 3),
            ShortcutKeyDisplayString = string.IsNullOrWhiteSpace(shortcut) ? null : shortcut,
        };
        if (glyph != null)
        {
            item.Image = UiKit.GlyphBitmap(glyph, Theme.Text, S(18));
            item.ImageScaling = ToolStripItemImageScaling.None;
        }

        item.Click += (_, _) => action();
        return item;
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
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
            _tray = null;
        }

        _clipboard.Dispose();
        Application.Exit();
    }

    private void SetStatus(string message)
    {
        if (!IsDisposed && _status != null)
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
