using CaptureDemo.Core;
using CaptureDemo.Native;
using CaptureDemo.Ui;

namespace CaptureDemo;

internal sealed class SettingsForm : ShellForm
{
    private const int WindowWidth = 600;

    private readonly FieldBox _awaken;
    private readonly FieldBox _element;
    private readonly FieldBox _long;
    private readonly FieldBox _clipboard;
    private readonly FieldBox _directory;
    private readonly FieldBox _pattern;
    private readonly ToggleSwitch _copyToClipboard;
    private readonly ToggleSwitch _watchClipboard;
    private readonly ToggleSwitch _startWithWindows;
    private readonly ToggleSwitch _closeToTray;
    private readonly Picker _limit;
    private readonly Picker _speed;
    private readonly Picker _sample;
    private readonly Picker _appearance;
    private readonly ScrollSheet _scroll;
    private readonly ContactBlock _contact;

    private SettingsForm(AppSettings settings)
    {
        Text = "设置";
        Icon = IconFactory.AppIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        // Fixed height on purpose: the sheet scrolls instead of the window growing with
        // every new setting, so adding a row never changes the window's footprint.
        var work = Screen.FromPoint(Cursor.Position).WorkingArea;
        var width = S(WindowWidth);
        var height = Math.Min(S(672), Math.Max(S(420), work.Height - S(72)));
        ClientSize = new Size(width, height);

        // Explicit stacking: a docked Fill plus a docked Bottom let the Fill claim the
        // whole client area first, leaving the footer to paint over live content.
        _scroll = new ScrollSheet();
        Controls.Add(_scroll);

        var footer = new BufferedPanel
        {
            Height = S(60),
            BackColor = Theme.AppBack,
        };
        // A hairline keeps the scrolling content from bleeding into the button strip.
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Divider);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        Controls.Add(footer);

        var sheet = _scroll.Sheet;

        _awaken = HotkeyBox(settings.AwakenHotkey);
        _element = HotkeyBox(settings.ElementHotkey);
        _long = HotkeyBox(settings.LongHotkey);
        _clipboard = HotkeyBox(settings.ClipboardHotkey);
        _directory = Field(settings.SaveDirectory, $@"留空则用 图片\{AppInfo.Product}");
        _pattern = Field(settings.FilenamePattern);
        _copyToClipboard = Toggle(settings.CopyToClipboard);
        _watchClipboard = Toggle(settings.WatchClipboard);
        _startWithWindows = Toggle(StartupRegistration.IsEnabled());
        _closeToTray = Toggle(settings.CloseToTray);
        _limit = Pick(new[] { "5 条", "8 条", "12 条", "16 条", "20 条", "30 条" }, LimitIndex(settings.ClipboardLimit));
        _speed = Pick(Enumerable.Range(1, 10).Select(value => value + " 级").ToArray(), Math.Clamp(settings.ScrollSpeed, 1, 10) - 1);
        _sample = Pick(new[] { "60 ms", "80 ms", "120 ms", "160 ms", "200 ms", "300 ms" }, SampleIndex(settings.SampleMilliseconds));
        _appearance = Pick(new[] { "跟随系统", "浅色", "深色" }, settings.Appearance switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0,
        });

        var left = S(24);
        var cardWidth = S(WindowWidth) - (S(24) * 2);
        var top = S(18);

        top = AddCard(sheet, "快捷键", top, left, cardWidth, new (string, Control)[]
        {
            ("唤醒截图条", _awaken),
            ("元素截图", _element),
            ("长截图", _long),
            ("剪贴板历史", _clipboard),
        });

        top = AddCard(sheet, "保存", top, left, cardWidth, new (string, Control)[]
        {
            ("保存目录", DirectoryRow()),
            ("文件名模板", _pattern),
            ("复制到剪贴板", _copyToClipboard),
        });

        top = AddCard(sheet, "剪贴板", top, left, cardWidth, new (string, Control)[]
        {
            ("记录剪贴板", _watchClipboard),
            ("保留条数", _limit),
        });

        top = AddCard(sheet, "长截图", top, left, cardWidth, new (string, Control)[]
        {
            ("滚动速度", _speed),
            ("采样间隔", _sample),
        });

        top = AddCard(sheet, "常规", top, left, cardWidth, new (string, Control)[]
        {
            ("外观", _appearance),
            ("开机自动启动", _startWithWindows),
            ("关闭窗口后常驻托盘", _closeToTray),
        });

        var stamp = new Label
        {
            Text = AppInfo.Label,
            Font = UiFonts.Small,
            ForeColor = Theme.Muted,
            BackColor = Theme.AppBack,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        stamp.SetBounds(left, top, cardWidth, S(18));
        sheet.Controls.Add(stamp);

        // Feedback channels live directly under the version stamp: whoever scrolls to
        // the number is looking for "which build is this", and the next question is
        // usually "how do I report a problem with it".
        var contact = new ContactBlock(
            DpiScale,
            AppInfo.SupportCaption,
            ("Telegram", AppInfo.ContactTelegram),
            ("Email", AppInfo.ContactEmail));
        contact.SetBounds(left, top, cardWidth, contact.Height);
        sheet.Controls.Add(contact);
        _contact = contact;

        // Sits at the very end of the sheet: readable only after scrolling, and it is
        // the natural place for the licence line and update notice later on.
        var viewport = ClientSize.Height - footer.Height;
        var tail = stamp.Height + S(12) + contact.Height;
        var natural = top + tail + S(22);
        var contentHeight = Math.Max(natural, viewport);
        contact.Top = contentHeight - S(22) - contact.Height;
        stamp.Top = contact.Top - S(12) - stamp.Height;
        _scroll.SetContentHeight(contentHeight);

        BuildFooter(footer);

        void Stack()
        {
            var bar = footer.Height;
            footer.SetBounds(0, ClientSize.Height - bar, ClientSize.Width, bar);
            _scroll.SetBounds(0, 0, ClientSize.Width, Math.Max(1, ClientSize.Height - bar));
        }

        Stack();
        SizeChanged += (_, _) => Stack();

        Shown += (_, _) =>
        {
            _awaken.Inner.Focus();
            _awaken.Inner.SelectionLength = 0;
        };

        KeyPreview = true;
    }

    public static SettingsForm CreateForPreview(AppSettings settings) => new(settings);

    /// <summary>Dev-only: parks the sheet at its end so the version stamp can be reviewed.</summary>
    internal void ScrollToEndForPreview() => _scroll.ScrollToEnd();

    /// <summary>Dev-only: triggers the "copied" flash so its position can be reviewed.</summary>
    internal void FlashContactCopyForPreview() => _contact.CopyRow(1);

    internal string DebugScroll() =>
        _scroll.Describe() + " | client=" + ClientSize +
        " | " + string.Join("; ", Controls.Cast<Control>().Select(c => $"{c.GetType().Name}/{c.Dock}={c.Bounds}"));

    public static bool TryEdit(IWin32Window owner, AppSettings settings)
    {
        using var form = new SettingsForm(settings);
        var result = owner is Form host && host.Visible ? form.ShowDialog(host) : form.ShowDialog();
        if (result != DialogResult.OK)
        {
            return false;
        }

        settings.AwakenHotkey = form._awaken.Text.Trim();
        settings.ElementHotkey = form._element.Text.Trim();
        settings.LongHotkey = form._long.Text.Trim();
        settings.ClipboardHotkey = form._clipboard.Text.Trim();
        settings.SaveDirectory = form._directory.Text.Trim();
        settings.FilenamePattern = form._pattern.Text.Trim();
        settings.CopyToClipboard = form._copyToClipboard.Checked;
        settings.WatchClipboard = form._watchClipboard.Checked;
        settings.ClipboardLimit = LimitValue(form._limit.SelectedIndex);
        settings.ScrollSpeed = form._speed.SelectedIndex + 1;
        settings.SampleMilliseconds = SampleValue(form._sample.SelectedIndex);
        settings.LaunchAtStartup = form._startWithWindows.Checked;
        settings.CloseToTray = form._closeToTray.Checked;
        settings.Appearance = form._appearance.SelectedIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "System",
        };
        settings.Normalize();
        return true;
    }

    private void BuildFooter(Panel footer)
    {
        var reset = new FlatButton
        {
            Text = "恢复默认",
            Variant = ButtonVariant.Ghost,
            CornerRadius = S(8),
        };
        reset.SetBounds(S(24), S(12), S(104), S(36));
        reset.Clicked += (_, _) => ResetToDefaults();
        footer.Controls.Add(reset);

        var save = new FlatButton
        {
            Text = "保存",
            Variant = ButtonVariant.Primary,
            CornerRadius = S(8),
        };
        save.SetBounds(S(WindowWidth) - S(24) - S(96), S(12), S(96), S(36));
        save.Clicked += (_, _) => TryAccept();
        footer.Controls.Add(save);

        var cancel = new FlatButton
        {
            Text = "取消",
            Variant = ButtonVariant.Secondary,
            CornerRadius = S(8),
        };
        cancel.SetBounds(S(WindowWidth) - S(24) - S(96) - S(88), S(12), S(80), S(36));
        cancel.Clicked += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        footer.Controls.Add(cancel);
    }

    private int AddCard(
        Control host,
        string title,
        int top,
        int left,
        int width,
        (string Label, Control Editor)[] rows)
    {
        var header = S(34);
        var rowHeight = S(44);
        var height = header + (rows.Length * rowHeight) + S(8);
        var card = new CardPanel { Radius = S(12) };
        card.SetBounds(left, top, width, height);
        host.Controls.Add(card);

        var titleLabel = new Label
        {
            Text = title,
            Font = UiFonts.Section,
            ForeColor = Theme.Muted,
            BackColor = Theme.Card,
            AutoSize = false,
        };
        titleLabel.SetBounds(S(18), S(12), width - S(36), S(20));
        card.Controls.Add(titleLabel);

        // Wide enough for the longest label ("关闭窗口后常驻托盘") to stay on one line.
        var labelWidth = S(136);
        for (var i = 0; i < rows.Length; i++)
        {
            var y = header + (i * rowHeight);
            var label = new Label
            {
                Text = rows[i].Label,
                Font = UiFonts.Body,
                ForeColor = Theme.Text,
                BackColor = Theme.Card,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            label.SetBounds(S(18), y, labelWidth, rowHeight);
            card.Controls.Add(label);

            var editor = rows[i].Editor;
            var editorLeft = S(18) + labelWidth;
            if (editor is ToggleSwitch toggle)
            {
                // Switches keep their own footprint instead of stretching across the row;
                // left-aligning them keeps every editor edge on one vertical line.
                toggle.SetBounds(
                    editorLeft,
                    y + ((rowHeight - toggle.Height) / 2),
                    toggle.Width,
                    toggle.Height);
            }
            else
            {
                var editorWidth = width - editorLeft - S(18);
                var editorHeight = S(34);
                editor.SetBounds(
                    editorLeft,
                    y + ((rowHeight - editorHeight) / 2),
                    editorWidth,
                    editorHeight);
            }

            card.Controls.Add(editor);
        }

        return top + height + S(12);
    }

    private Control DirectoryRow()
    {
        var panel = new Panel { BackColor = Theme.Card, Height = S(34) };
        var browse = new FlatButton
        {
            Text = "浏览…",
            Variant = ButtonVariant.Secondary,
            CornerRadius = S(8),
            Font = UiFonts.Small,
        };
        browse.Clicked += (_, _) => Browse();
        panel.Controls.Add(_directory);
        panel.Controls.Add(browse);
        panel.Resize += (_, _) =>
        {
            var buttonWidth = S(78);
            browse.SetBounds(panel.Width - buttonWidth, 0, buttonWidth, panel.Height);
            _directory.SetBounds(0, 0, Math.Max(S(40), panel.Width - buttonWidth - S(8)), panel.Height);
        };
        return panel;
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择截图保存目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_directory.Text) ? _directory.Text : AppSettings.DefaultDirectory(),
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _directory.Text = dialog.SelectedPath;
        }
    }

    private void TryAccept()
    {
        if (!HotkeyChord.TryParse(_awaken.Text, out _) ||
            !HotkeyChord.TryParse(_element.Text, out _) ||
            !HotkeyChord.TryParse(_long.Text, out _) ||
            !HotkeyChord.TryParse(_clipboard.Text, out _))
        {
            MessageBox.Show(this, "热键需要包含一个主键，例如 Ctrl+Alt+S。", "热键无效");
            return;
        }

        if (string.IsNullOrWhiteSpace(_pattern.Text))
        {
            _pattern.Text = AppSettings.DefaultFilenamePattern;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void ResetToDefaults()
    {
        _awaken.Text = AppSettings.DefaultAwakenHotkey;
        _element.Text = AppSettings.DefaultElementHotkey;
        _long.Text = AppSettings.DefaultLongHotkey;
        _clipboard.Text = AppSettings.DefaultClipboardHotkey;
        _directory.Text = "";
        _pattern.Text = AppSettings.DefaultFilenamePattern;
        _copyToClipboard.Checked = true;
        _watchClipboard.Checked = true;
        _closeToTray.Checked = true;
        _limit.SelectedIndex = LimitIndex(12);
        _speed.SelectedIndex = 0;
        _sample.SelectedIndex = SampleIndex(120);
        _appearance.SelectedIndex = 0;
    }

    private FieldBox Field(string text, string placeholder = "")
    {
        var box = new FieldBox { Placeholder = placeholder, Radius = S(8) };
        box.Text = text;
        return box;
    }

    private ToggleSwitch Toggle(bool value)
    {
        var toggle = new ToggleSwitch { Size = new Size(S(44), S(24)) };
        toggle.SetCheckedSilently(value);
        return toggle;
    }

    private static Picker Pick(string[] items, int selected)
    {
        var picker = new Picker();
        picker.SetItems(items, Math.Clamp(selected, 0, items.Length - 1));
        return picker;
    }

    private FieldBox HotkeyBox(string text)
    {
        var box = new FieldBox { ReadOnly = true, Radius = S(8) };
        box.Text = text;
        box.Inner.ShortcutsEnabled = false;
        box.Inner.Cursor = Cursors.Hand;
        // WinForms selects the whole value when a read-only box takes focus, which read
        // as a stray blue block on screen. Deselect after the focus message settles.
        box.Inner.GotFocus += (_, _) => box.Inner.BeginInvoke(new Action(() => box.Inner.Select(0, 0)));
        box.Inner.KeyDown += (_, e) =>
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            if (e.KeyCode is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin or Keys.Escape)
            {
                return;
            }

            var parts = new List<string>();
            if (e.Control)
            {
                parts.Add("Ctrl");
            }

            if (e.Alt)
            {
                parts.Add("Alt");
            }

            if (e.Shift)
            {
                parts.Add("Shift");
            }

            parts.Add(KeyName(e.KeyCode));
            box.Text = string.Join("+", parts);
        };
        return box;
    }

    private static string KeyName(Keys key)
    {
        if (key is >= Keys.D0 and <= Keys.D9)
        {
            return ((char)('0' + (key - Keys.D0))).ToString();
        }

        if (key is >= Keys.NumPad0 and <= Keys.NumPad9)
        {
            return ((char)('0' + (key - Keys.NumPad0))).ToString();
        }

        return key.ToString();
    }

    private static int LimitIndex(int value) => value switch
    {
        <= 5 => 0,
        <= 8 => 1,
        <= 12 => 2,
        <= 16 => 3,
        <= 20 => 4,
        _ => 5,
    };

    private static int LimitValue(int index) => index switch
    {
        0 => 5,
        1 => 8,
        2 => 12,
        3 => 16,
        4 => 20,
        _ => 30,
    };

    private static int SampleIndex(int value) => value switch
    {
        <= 60 => 0,
        <= 80 => 1,
        <= 120 => 2,
        <= 160 => 3,
        <= 200 => 4,
        _ => 5,
    };

    private static int SampleValue(int index) => index switch
    {
        0 => 60,
        1 => 80,
        2 => 120,
        3 => 160,
        4 => 200,
        _ => 300,
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }
}
