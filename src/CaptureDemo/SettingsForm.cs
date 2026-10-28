using CaptureDemo.Core;

namespace CaptureDemo;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _awaken;
    private readonly TextBox _element;
    private readonly TextBox _long;
    private readonly TextBox _directory;
    private readonly TextBox _pattern;
    private readonly CheckBox _clipboard;
    private readonly NumericUpDown _speed;
    private readonly NumericUpDown _sample;

    private SettingsForm(AppSettings settings)
    {
        Text = "设置";
        Font = UiFonts.Body;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(640, 520);
        MinimumSize = new Size(640, 520);
        BackColor = Theme.FluentBack;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.FluentBack,
            Padding = new Padding(28, 20, 28, 16),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = Color.White,
            Padding = new Padding(20, 16, 20, 12),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _awaken = HotkeyBox(settings.AwakenHotkey);
        _element = HotkeyBox(settings.ElementHotkey);
        _long = HotkeyBox(settings.LongHotkey);
        _directory = Field(settings.SaveDirectory);
        _pattern = Field(settings.FilenamePattern);
        _clipboard = new CheckBox
        {
            Text = "保存时复制到剪贴板",
            Checked = settings.CopyToClipboard,
            AutoSize = true,
            ForeColor = Theme.Text,
            Margin = new Padding(0, 8, 0, 4),
        };
        _speed = Number(Math.Clamp(settings.ScrollSpeed, 1, 10), 1, 10, 1);
        _sample = Number(Math.Clamp(settings.SampleMilliseconds, 40, 800), 40, 800, 20);

        var row = 0;
        row = Section(grid, row, "快捷键");
        row = Pair(grid, row, "唤醒热键", _awaken);
        row = Pair(grid, row, "元素热键", _element);
        row = Pair(grid, row, "长截图热键", _long);
        row = Section(grid, row, "保存");
        row = Pair(grid, row, "保存目录", DirectoryRow());
        row = Pair(grid, row, "文件名", _pattern);
        grid.Controls.Add(_clipboard, 1, row);
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        row++;
        row = Section(grid, row, "长截图");
        row = Pair(grid, row, "滚动速度", _speed);
        Pair(grid, row, "采样间隔", _sample);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Theme.FluentBack,
            Padding = new Padding(0, 8, 0, 0),
        };
        var ok = new Button
        {
            Text = "保存",
            AutoSize = true,
            MinimumSize = new Size(88, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Accent,
            ForeColor = Color.White,
            Margin = new Padding(8, 0, 0, 0),
        };
        ok.FlatAppearance.BorderSize = 0;
        var cancel = new Button
        {
            Text = "取消",
            AutoSize = true,
            MinimumSize = new Size(88, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Theme.Text,
            DialogResult = DialogResult.Cancel,
        };
        cancel.FlatAppearance.BorderColor = Theme.CardBorder;
        ok.Click += (_, _) => TryAccept();
        footer.Controls.Add(ok);
        footer.Controls.Add(cancel);

        root.Controls.Add(grid, 0, 0);
        root.Controls.Add(footer, 0, 1);
        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
    }

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
        settings.SaveDirectory = form._directory.Text.Trim();
        settings.FilenamePattern = form._pattern.Text.Trim();
        settings.CopyToClipboard = form._clipboard.Checked;
        settings.ScrollSpeed = (int)form._speed.Value;
        settings.SampleMilliseconds = (int)form._sample.Value;
        settings.Normalize();
        return true;
    }

    private void TryAccept()
    {
        if (!HotkeyChord.TryParse(_awaken.Text, out _) ||
            !HotkeyChord.TryParse(_element.Text, out _) ||
            !HotkeyChord.TryParse(_long.Text, out _))
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

    private Control DirectoryRow()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new Padding(0),
            Height = 32,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _directory.Dock = DockStyle.Fill;
        _directory.Margin = new Padding(0, 4, 8, 0);
        var browse = new Button
        {
            Text = "浏览…",
            AutoSize = true,
            MinimumSize = new Size(84, 28),
            Margin = new Padding(0, 2, 0, 0),
            Anchor = AnchorStyles.Right,
        };
        browse.Click += (_, _) =>
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
        };
        panel.Controls.Add(_directory, 0, 0);
        panel.Controls.Add(browse, 1, 0);
        return panel;
    }

    private static int Section(TableLayoutPanel grid, int row, string title)
    {
        var label = new Label
        {
            Text = title,
            Font = UiFonts.Section,
            ForeColor = Theme.Text,
            AutoSize = true,
            Margin = new Padding(0, row == 0 ? 0 : 16, 0, 6),
        };
        grid.Controls.Add(label, 0, row);
        grid.SetColumnSpan(label, 2);
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return row + 1;
    }

    private static int Pair(TableLayoutPanel grid, int row, string caption, Control editor)
    {
        grid.Controls.Add(new Label
        {
            Text = caption,
            AutoSize = false,
            Size = new Size(140, 28),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Text,
            Margin = new Padding(0, 6, 12, 0),
        }, 0, row);
        editor.Margin = new Padding(0, 4, 0, 0);
        editor.Dock = DockStyle.Fill;
        grid.Controls.Add(editor, 1, row);
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        return row + 1;
    }

    private static TextBox Field(string text) => new()
    {
        Text = text,
        BorderStyle = BorderStyle.FixedSingle,
        Height = 28,
    };

    private static NumericUpDown Number(int value, int min, int max, int step) => new()
    {
        Minimum = min,
        Maximum = max,
        Increment = step,
        Value = value,
        Width = 96,
        TextAlign = HorizontalAlignment.Left,
    };

    private static TextBox HotkeyBox(string text)
    {
        var box = Field(text);
        box.ReadOnly = true;
        box.ShortcutsEnabled = false;
        box.KeyDown += (_, e) =>
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
}
