using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CaptureDemo.Clips;
using CaptureDemo.Core;
using CaptureDemo.Ui;

namespace CaptureDemo.Dev;

/// <summary>
/// Dev-only: renders the real forms to PNG so the UI can be reviewed without
/// clicking through the app. Run with <c>--ui-demo &lt;folder&gt;</c>.
/// Nothing here is reachable from normal startup.
/// </summary>
internal static class UiDemo
{
    public static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var settings = AppSettings.Load(AppSettings.DefaultFilePath());

        foreach (var appearance in new[] { Appearance.Light, Appearance.Dark })
        {
            settings.Appearance = appearance == Appearance.Light ? "Light" : "Dark";
            Theme.Apply(appearance);
            var tag = appearance == Appearance.Light ? "light" : "dark";

            using (var main = new MainForm(LaunchMode.Menu, preview: true, settingsOverride: settings))
            {
                main.Show();
                Pump();
                Save(main, Path.Combine(folder, $"main-{tag}.png"));
                using (var tray = main.BuildTrayMenu())
                {
                    SaveMenu(tray, main, Path.Combine(folder, $"tray-{tag}.png"));
                }

                main.Hide();
            }

            Theme.Apply(appearance);
            using (var form = SettingsForm.CreateForPreview(settings))
            {
                form.Show();
                Pump();
                Save(form, Path.Combine(folder, $"settings-{tag}.png"));
                form.Hide();
            }

            var store = new ClipboardStore(Path.Combine(Path.GetTempPath(), "capturedemo-ui-demo", tag));
            if (Directory.Exists(store.Folder))
            {
                Directory.Delete(store.Folder, true);
            }

            using (var service = new ClipboardService(settings, listen: false, store: store))
            {
                Seed(service);
                using var panel = new ClipboardForm(service);
                using var composed = panel.Snapshot();
                using var framed = new Bitmap(composed.Width + 40, composed.Height + 40, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(framed))
                {
                    g.Clear(Theme.IsDark ? Color.FromArgb(18, 19, 22) : Color.FromArgb(214, 216, 221));
                    g.DrawImage(composed, 20, 20);
                }

                framed.Save(Path.Combine(folder, $"clipboard-{tag}.png"), ImageFormat.Png);

                var sample = service.History.Ordered().FirstOrDefault(entry => entry.Kind == ClipboardKind.Image);
                if (sample != null)
                {
                    // A drop-down closes as soon as it loses activation, so it needs a
                    // visible owner on screen while the pixels are copied.
                    panel.Show();
                    Pump();
                    using var rowMenu = panel.BuildItemMenuForPreview(sample);
                    SaveMenu(rowMenu, panel, Path.Combine(folder, $"clipmenu-{tag}.png"));
                    panel.Hide();
                }
            }
        }
    }

    private static void Seed(ClipboardService service)
    {
        var history = service.History;
        history.Push(new ClipboardEntry
        {
            Id = "seed-file",
            Kind = ClipboardKind.Files,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-42),
            Signature = "seed:1",
            Files = new List<string> { @"D:\设计稿\2026-10 品牌视觉\封面-v3.psd", @"D:\设计稿\2026-10 品牌视觉\封面-v3.png" },
        });
        history.Push(new ClipboardEntry
        {
            Id = "seed-text",
            Kind = ClipboardKind.Text,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-18),
            Signature = "seed:2",
            Text = "M12 3.5c4.7 0 8.5 2.9 8.5 6.5s-3.8 6.5-8.5 6.5",
        });
        var pinned = new ClipboardEntry
        {
            Id = "seed-pinned",
            Kind = ClipboardKind.Text,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-9),
            Signature = "seed:3",
            Text = "support@example.com",
        };
        history.Push(pinned);
        history.TogglePin(pinned.Id);

        var image = new ClipboardEntry
        {
            Id = "seed-image",
            Kind = ClipboardKind.Image,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-3),
            Signature = "seed:4",
        };
        using (var demo = DemoBitmap())
        {
            service.Store.SaveImage(image, demo);
        }

        history.Push(image);
        history.Push(new ClipboardEntry
        {
            Id = "seed-text-2",
            Kind = ClipboardKind.Text,
            CreatedUtc = DateTime.UtcNow.AddSeconds(-20),
            Signature = "seed:5",
            Text = "截图条顶栏六模式：全屏 / 矩形 / 窗口 / 长截图 / 元素 / 任意形状",
        });
    }

    private static Bitmap DemoBitmap()
    {
        var bitmap = new Bitmap(1280, 720);
        using var g = Graphics.FromImage(bitmap);
        using var brush = new LinearGradientBrush(
            new Rectangle(0, 0, 1280, 720),
            Color.FromArgb(58, 92, 168),
            Color.FromArgb(148, 88, 156),
            LinearGradientMode.ForwardDiagonal);
        g.FillRectangle(brush, 0, 0, 1280, 720);
        using var light = new SolidBrush(Color.FromArgb(52, 255, 255, 255));
        g.FillEllipse(light, 760, 120, 420, 420);
        g.FillEllipse(light, 180, 380, 260, 260);
        return bitmap;
    }

    private static void Pump()
    {
        Application.DoEvents();
        Thread.Sleep(60);
        Application.DoEvents();
    }

    /// <summary>Popups are separate windows, so screen pixels are the only faithful source.</summary>
    private static void SaveMenu(ContextMenuStrip menu, Control owner, string path)
    {
        Native.NativeMethods.SetForegroundWindow(owner.Handle);
        menu.Show(owner, new Point(owner.ClientSize.Width / 3, owner.ClientSize.Height / 3));
        Pump();
        Thread.Sleep(160);
        Application.DoEvents();

        var bounds = menu.Bounds;
        using var bitmap = new Bitmap(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height), PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }

        bitmap.Save(path, ImageFormat.Png);
        menu.Close();
        Pump();
    }

    private static void Save(Form form, string path)
    {
        // Real screen pixels: the transparent, self-painted panels do not survive WM_PRINT.
        var topMost = form.TopMost;
        form.TopMost = true;
        Native.NativeMethods.SetForegroundWindow(form.Handle);
        form.Activate();
        form.Refresh();
        Application.DoEvents();
        Thread.Sleep(260);
        Application.DoEvents();

        var bounds = form.Bounds;
        using var bitmap = new Bitmap(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height), PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }

        bitmap.Save(path, ImageFormat.Png);
        form.TopMost = topMost;
    }
}
