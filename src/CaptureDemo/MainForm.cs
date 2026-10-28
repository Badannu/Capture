using System.Runtime.InteropServices;
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
    private readonly HotkeyAuditor _auditor = new();

    /// <summary>Hotkeys Windows refused, with the row they belong to. Empty is the good case.</summary>
    private readonly List<(string Action, HotkeyConflict Conflict)> _hotkeyProblems = new();

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
        _settings = settingsOverride ?? AppSettings.LoadForApp();
        Theme.Apply(ParseAppearance(_settings.Appearance));
        BackColor = Theme.AppBack;
        ForeColor = Theme.Text;
        _clipboard = new ClipboardService(_settings, listen: !preview, store: null);
        _clipboard.Changed += UpdateClipboardCard;

        Text = AppInfo.Product;
        Icon = IconFactory.AppIcon();
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        KeyPreview = true;
        ClientSize = new Size(S(480), S(410));
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
        ApplyHotkeyStatus();
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
            if (_settings.CloseToTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            // "不常驻" makes closing the window the quit gesture: the global hotkeys have to
            // be released, otherwise the process would linger invisibly and keep them.
            e.Cancel = true;
            _allowExit = true;
            BeginInvoke((Action)ExitApp);
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotkeys();
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The auditor owns a message-only probe window; leaving it alive would keep a
            // thread-less window handle around after the form is gone.
            _auditor.Dispose();
        }

        base.Dispose(disposing);
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

        // The status line sits below the buttons, hugging the bottom edge: between the
        // clipboard card and the buttons it forced an uneven gap whenever it was empty,
        // which is most of the time.
        _status = new Label
        {
            Text = "",
            Font = UiFonts.Small,
            ForeColor = Theme.Muted,
            BackColor = Theme.AppBack,
            AutoEllipsis = true,
            AutoSize = false,
        };
        _status.SetBounds(pad, S(368), width, S(18));
        Controls.Add(_status);

        _settingsButton = new FlatButton
        {
            Text = "设置",
            Variant = ButtonVariant.Secondary,
            Glyph = SnipIcons.Sliders,
            GlyphSize = S(16),
            CornerRadius = S(8),
        };
        _settingsButton.SetBounds(pad, S(324), S(96), S(36));
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
        _folderButton.SetBounds(pad + S(104), S(324), S(152), S(36));
        _folderButton.Clicked += (_, _) => OpenFolder();
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

        // The old subtitle ("hotkey · stays in the tray") is gone — residency is a
        // setting now — so the product name is centred against the 44 px mark instead
        // of sitting above an empty line.
        TextRenderer.DrawText(
            g,
            AppInfo.Product,
            UiFonts.Title,
            new Rectangle(tile.Right + S(14), tile.Top, S(300), tile.Height),
            Theme.Text,
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
        // BuildContent recreates the label, so the hotkey verdict has to be written again
        // or switching theme would silently drop the only notice that a hotkey is dead.
        ApplyHotkeyStatus();
        SeedTray();
        if (IsHandleCreated)
        {
            ApplyChrome(Handle);
        }
    }

    // ── capture entry points ────────────────────────────────────────────────
    /// <summary>
    /// Every entry point — hotkey, tray item, main-window chip — opens the same capsule
    /// with the requested mode already selected. Long and element capture are picked
    /// *in* that capsule now, instead of jumping straight into a separate picker.
    /// </summary>
    private async Task StartSnipAsync(SnipMode initial = SnipMode.Rectangle)
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
            var result = await SnipSession.RunAsync(_settings, initial);
            switch (result.Follow)
            {
                case SnipFollow.Long:
                    Apply(await LongCaptureSession.RunAsync(_settings, result.Preset));
                    break;
                case SnipFollow.Element:
                    Apply(await ElementCaptureSession.RunAsync(_settings, result.Preset, result.Note));
                    break;
                case SnipFollow.Settings:
                    _openSettings = true;
                    break;
                default:
                    Apply(result.Outcome);
                    break;
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

    private Task StartElementAsync() => StartSnipAsync(SnipMode.Element);

    private Task StartLongAsync() => StartSnipAsync(SnipMode.Long);

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

        // Let go of our own hotkeys while the dialog is open. Its probe asks Windows for
        // the same chords; if we kept holding them, every row would accuse itself.
        UnregisterHotkeys();

        // TryEdit writes straight into _settings, so re-registering right after it reflects
        // whatever was accepted — including the cancel path, where nothing changed.
        var accepted = SettingsForm.TryEdit(this, _settings);
        RegisterHotkeys();

        if (!accepted)
        {
            ApplyHotkeyStatus();
            return;
        }

        try
        {
            _settings.Save(AppSettings.DefaultFilePath());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("设置没能写入：" + ex.Message, warning: true);
            return;
        }

        _clipboard.ApplySettings();
        StartupRegistration.Set(_settings.LaunchAtStartup);
        if (!string.Equals(appearanceBefore, _settings.Appearance, StringComparison.Ordinal))
        {
            ApplyTheme();
        }

        // The conflict verdict must win: "设置已保存。" used to overwrite it on the very
        // next line, so a user who saved a clashing chord never learned it failed.
        ApplyHotkeyStatus(saved: true);
    }

    private void RegisterHotkeys()
    {
        UnregisterHotkeys();
        _hotkeyProblems.Clear();
        if (_hotkeyWindow == IntPtr.Zero)
        {
            return;
        }

        _snipHotkey = Register(HotkeySnip, "唤醒截图条", _settings.AwakenHotkey);
        _elementHotkey = Register(HotkeyElement, "元素截图", _settings.ElementHotkey);
        _longHotkey = Register(HotkeyLong, "长截图", _settings.LongHotkey);
        _clipboardHotkey = Register(HotkeyClipboard, "剪贴板历史", _settings.ClipboardHotkey);
    }

    /// <summary>
    /// Registers one hotkey and records *why* it failed. The old boolean-only version
    /// could say nothing beyond "something did not register", which left the user with no
    /// way to tell a typo from a program holding the chord.
    /// </summary>
    private bool Register(int id, string action, string text)
    {
        if (!HotkeyChord.TryParse(text, out var chord))
        {
            _hotkeyProblems.Add((action, new HotkeyConflict(HotkeyConflictKind.Invalid, (text ?? string.Empty).Trim())));
            return false;
        }

        var ok = NativeMethods.RegisterHotKey(
            _hotkeyWindow,
            id,
            chord.Modifiers | NativeMethods.ModNoRepeat,
            chord.VirtualKey);
        if (ok)
        {
            return true;
        }

        _hotkeyProblems.Add((action, _auditor.Explain(chord, Marshal.GetLastWin32Error())));
        return false;
    }

    /// <summary>Writes the hotkey verdict into the status line, which sits at the window bottom.</summary>
    /// <summary>Dev-only: paints representative hotkey failures so the bottom line can be reviewed.</summary>
    internal void ShowHotkeyProblemsForPreview()
    {
        _hotkeyProblems.Clear();
        _hotkeyProblems.Add(("长截图", new HotkeyConflict(HotkeyConflictKind.Taken, "Ctrl+Alt+A", "微信", "截图")));
        _hotkeyProblems.Add(("元素截图", new HotkeyConflict(HotkeyConflictKind.Duplicate, "Ctrl+Alt+S", "唤醒截图条")));
        ApplyHotkeyStatus();
        Invalidate();
    }

    private void ApplyHotkeyStatus(bool saved = false)
    {
        if (_hotkeyProblems.Count == 0)
        {
            if (saved)
            {
                SetStatus("设置已保存。");
            }

            return;
        }

        // Two names fit in the 432 px line; the rest collapse into a count, otherwise
        // AutoEllipsis would cut the second name in half.
        SetStatus((saved ? "设置已保存，但" : string.Empty) + HotkeyStatusText(), warning: true);
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

        // Any of the four failing means the window comes up: the status line at its bottom
        // is where the explanation lives, and a clipboard-only failure used to hide it.
        if (_hotkeyProblems.Count > 0)
        {
            ShowFromTray();
            _tray?.ShowBalloonTip(4200, AppInfo.Product, HotkeyStatusText(), ToolTipIcon.Warning);
            return;
        }

        HideToTray();
        _tray?.ShowBalloonTip(2600, AppInfo.Product, "已在托盘。左键打开主窗口，右键可用剪贴板、截图或退出。", ToolTipIcon.None);
    }

    /// <summary>
    /// One line in a 432 px label. A single failure gets its reason spelled out; two get
    /// bare names, because "被占用（疑似微信 截图）" twice overflows and AutoEllipsis would
    /// cut the second name mid-word.
    /// </summary>
    private string HotkeyStatusText()
    {
        if (_hotkeyProblems.Count == 0)
        {
            return string.Empty;
        }

        var (action, conflict) = _hotkeyProblems[0];
        if (_hotkeyProblems.Count == 1)
        {
            return conflict.Short(action);
        }

        var names = string.Join("、", _hotkeyProblems.Take(2).Select(p => $"{p.Action} {p.Conflict.Chord}"));
        return $"{_hotkeyProblems.Count} 个快捷键没能注册：{names}" + (_hotkeyProblems.Count > 2 ? " 等" : string.Empty);
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
            Text = AppInfo.Product,
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

    private void SetStatus(string message, bool warning = false)
    {
        if (!IsDisposed && _status != null)
        {
            _status.Text = message;
            _status.ForeColor = warning ? Theme.Danger : Theme.Muted;
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
