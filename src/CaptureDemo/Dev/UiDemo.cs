using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CaptureDemo.Clips;
using CaptureDemo.Core;
using CaptureDemo.Snip;
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
                form.ScrollToEndForPreview();
                Pump();
                File.AppendAllText(
                    Path.Combine(folder, "scroll.txt"),
                    $"settings-{tag}: {form.DebugScroll()}{Environment.NewLine}contact: {form.DebugContact()}{Environment.NewLine}");
                Save(form, Path.Combine(folder, $"settings-{tag}-bottom.png"));
                form.FlashContactCopyForPreview();
                Pump();
                Save(form, Path.Combine(folder, $"settings-{tag}-contact.png"));
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

            SaveCaptureBar(folder, tag);
            SaveAnnotationBar(folder, tag);
            SaveAnnotationSample(folder, tag);
        }
    }

    /// <summary>
    /// The capsule in each of the three states that matter: default rectangle, long
    /// capture picked, element picked. The picked plate has to stay readable on a still
    /// frame, since it is no longer a momentary flash.
    /// </summary>
    private static void SaveCaptureBar(string folder, string tag)
    {
        var work = Screen.FromPoint(Cursor.Position).WorkingArea;
        foreach (var mode in new[] { SnipMode.Rectangle, SnipMode.Long, SnipMode.Element })
        {
            using var bar = new CapturePillForm();
            bar.SetShortcuts(AppSettings.DefaultLongHotkey, AppSettings.DefaultElementHotkey);
            bar.SetMode(mode);
            bar.Place(work, null);
            bar.Show();
            Pump();
            SaveLayered(bar, Path.Combine(folder, $"capture-{mode.ToString().ToLowerInvariant()}-{tag}.png"));
            bar.Hide();
        }
    }

    private static void SaveAnnotationBar(string folder, string tag)
    {
        var work = Screen.FromPoint(Cursor.Position).WorkingArea;
        using (var bar = new AnnoPillForm())
        {
            bar.Place(work, null);
            bar.Show();
            Pump();
            SaveLayered(bar, Path.Combine(folder, $"anno-{tag}.png"));
            bar.Hide();
        }

        using (var colors = new StylePillForm())
        {
            colors.ShowColors(AnnoPalette.Colors[0].ToArgb());
            colors.Place(work, null);
            colors.Show();
            Pump();
            SaveLayered(colors, Path.Combine(folder, $"anno-colors-{tag}.png"));
            colors.Hide();
        }

        using (var weights = new StylePillForm())
        {
            weights.ShowWeights(AnnoPalette.Weights[2]);
            weights.Place(work, null);
            weights.Show();
            Pump();
            SaveLayered(weights, Path.Combine(folder, $"anno-weights-{tag}.png"));
            weights.Hide();
        }
    }

    /// <summary>
    /// Every tool painted once onto a fake page, plus a selected item with its grips.
    /// Exercises the same <see cref="AnnoPainter"/> the live overlay and the export use.
    /// </summary>
    private static void SaveAnnotationSample(string folder, string tag)
    {
        const int width = 900;
        const int height = 500;
        using var canvas = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.FromArgb(246, 247, 249));
            using var head = new SolidBrush(Color.FromArgb(24, 26, 32));
            g.FillRectangle(head, 0, 0, width, 64);
            using var title = new SolidBrush(Color.FromArgb(232, 236, 242));
            g.FillRectangle(title, 32, 22, 180, 20);
            using var line = new SolidBrush(Color.FromArgb(206, 212, 220));
            for (var i = 0; i < 6; i++)
            {
                g.FillRectangle(line, 32, 108 + (i * 34), 380 - (i % 3 * 46), 12);
            }

            using var card = new SolidBrush(Color.FromArgb(255, 255, 255));
            g.FillRectangle(card, 470, 96, 380, 300);
            using var cardEdge = new Pen(Color.FromArgb(226, 230, 238));
            g.DrawRectangle(cardEdge, 470, 96, 380, 300);
            var noise = new Random(7);
            for (var i = 0; i < 180; i++)
            {
                using var pixel = new SolidBrush(Color.FromArgb(
                    60 + noise.Next(150),
                    60 + noise.Next(150),
                    60 + noise.Next(150)));
                g.FillRectangle(pixel, 486 + noise.Next(330), 112 + noise.Next(268), 16, 16);
            }
        }

        using var source = (Bitmap)canvas.Clone();
        using (var g = Graphics.FromImage(canvas))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var arrow = AnnoItem.FromDrag(AnnoTool.Arrow, new Point(300, 420), new Point(560, 150));
            arrow.ColorArgb = AnnoPalette.Colors[0].ToArgb();
            arrow.Width = 6.5f;
            AnnoPainter.Draw(g, arrow, source);

            var pen = new AnnoItem(
                AnnoTool.Pen,
                new[] { 60, 90, 130, 175, 215, 250 },
                new[] { 300, 268, 318, 282, 330, 300 }) { ColorArgb = AnnoPalette.Colors[5].ToArgb(), Width = 6.5f };
            AnnoPainter.Draw(g, pen, source);

            var straight = AnnoItem.FromDrag(AnnoTool.Line, new Point(60, 380), new Point(250, 380));
            straight.ColorArgb = AnnoPalette.Colors[4].ToArgb();
            straight.Width = 4f;
            AnnoPainter.Draw(g, straight, source);

            var highlight = new AnnoItem(AnnoTool.Highlight, new[] { 30, 420 }, new[] { 232, 232 })
            {
                ColorArgb = AnnoPalette.Colors[2].ToArgb(),
                Width = 6.5f,
            };
            AnnoPainter.Draw(g, highlight, source);

            var circle = AnnoItem.FromDrag(AnnoTool.Circle, new Point(292, 118), new Point(392, 196));
            circle.ColorArgb = AnnoPalette.Colors[3].ToArgb();
            circle.Width = 4f;
            AnnoPainter.Draw(g, circle, source);

            var ellipse = AnnoItem.FromDrag(AnnoTool.Ellipse, new Point(70, 90), new Point(250, 170));
            ellipse.ColorArgb = AnnoPalette.Colors[1].ToArgb();
            ellipse.Width = 4f;
            AnnoPainter.Draw(g, ellipse, source);

            var mosaic = new AnnoItem(AnnoTool.Mosaic, new[] { 600, 760 }, new[] { 300, 370 });
            AnnoPainter.Draw(g, mosaic, source);

            // The selected item shows the dashed frame plus the eight grips.
            var selected = AnnoItem.FromDrag(AnnoTool.RoundRect, new Point(452, 80), new Point(866, 280));
            selected.ColorArgb = AnnoPalette.Colors[0].ToArgb();
            selected.Width = 4f;
            AnnoPainter.Draw(g, selected, source);
            AnnoPainter.DrawSelection(g, selected, 1f);
        }

        canvas.Save(Path.Combine(folder, $"anno-sample-{tag}.png"), ImageFormat.Png);
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

    /// <summary>Layered windows cannot be screen-grabbed, so read the frame they last presented.</summary>
    private static void SaveLayered(Form form, string path)
    {
        Application.DoEvents();
        using var frame = PillDraw.Snapshot(form);
        if (frame == null)
        {
            return;
        }

        using var framed = new Bitmap(frame.Width + 24, frame.Height + 24, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(framed))
        {
            g.Clear(Theme.IsDark ? Color.FromArgb(24, 25, 28) : Color.FromArgb(236, 238, 242));
            g.DrawImage(frame, 12, 12);
        }

        framed.Save(path, ImageFormat.Png);
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
