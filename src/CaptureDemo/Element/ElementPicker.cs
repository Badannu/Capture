using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace CaptureDemo.Element;

internal sealed record ElementSnapshot(Rectangle Bounds, string Caption, bool IsUseful, bool IsDesktop);

/// <summary>
/// Live UI Automation hit test. FlaUI's <c>FromPoint</c> is <c>ElementFromPoint</c>.
/// The parent chain is walked so the overlay can move the highlight up and down.
/// Our own process is skipped. No Snow Shot source is used.
/// </summary>
internal sealed class ElementPicker : IDisposable
{
    private readonly UIA3Automation _automation = new();
    private readonly int _ownPid = Environment.ProcessId;

    public IReadOnlyList<ElementSnapshot> CaptureChain(Point screen)
    {
        var leaf = ElementAt(screen);
        if (leaf == null)
        {
            return Array.Empty<ElementSnapshot>();
        }

        var list = new List<ElementSnapshot>(8);
        var seen = new HashSet<string>();
        var current = leaf;
        for (var guard = 0; guard < 32 && current != null; guard++)
        {
            var key = RuntimeKey(current);
            if (key != null && !seen.Add(key))
            {
                break;
            }

            var snapshot = TrySnapshot(current);
            if (snapshot != null)
            {
                list.Add(snapshot);
                if (snapshot.IsDesktop)
                {
                    break;
                }
            }

            current = NextParent(current);
        }

        return list;
    }

    public void Dispose() => _automation.Dispose();

    private AutomationElement? ElementAt(Point screen)
    {
        try
        {
            return _automation.FromPoint(screen);
        }
        catch
        {
            return null;
        }
    }

    private static AutomationElement? NextParent(AutomationElement current)
    {
        try
        {
            return current.Parent;
        }
        catch
        {
            return null;
        }
    }

    private ElementSnapshot? TrySnapshot(AutomationElement element)
    {
        try
        {
            if (element.Properties.ProcessId.TryGetValue(out var pid) && pid == _ownPid)
            {
                return null;
            }

            var className = ReadClassName(element);
            var isDesktop = className is "#32769" or "Progman" or "WorkerW";
            var name = Clean(ReadName(element));
            var typeName = "控件";
            if (element.Properties.ControlType.TryGetValue(out var controlType))
            {
                typeName = ControlTypeNames.ToChinese(controlType);
            }

            var bounds = element.BoundingRectangle;
            var onScreen = NativeMethods.VirtualScreen().IntersectsWith(bounds);
            var useful = !isDesktop && bounds.Width >= 2 && bounds.Height >= 2 && onScreen;
            var caption = string.IsNullOrEmpty(name) ? typeName : typeName + "  " + name;
            return new ElementSnapshot(bounds, caption, useful, isDesktop);
        }
        catch
        {
            return null;
        }
    }

    private static string? RuntimeKey(AutomationElement element)
    {
        try
        {
            if (element.Properties.RuntimeId.TryGetValue(out var id) && id is { Length: > 0 })
            {
                return string.Join(',', id);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string ReadName(AutomationElement element)
    {
        try
        {
            return element.Properties.Name.TryGetValue(out var name) ? name ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static string ReadClassName(AutomationElement element)
    {
        try
        {
            return element.Properties.ClassName.TryGetValue(out var name) ? name ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static string Clean(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        var text = name.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (text.Contains("  ", StringComparison.Ordinal))
        {
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        }

        return text.Length <= 48 ? text : text[..47] + "…";
    }
}

internal static class ControlTypeNames
{
    public static string ToChinese(ControlType type) => type switch
    {
        ControlType.Button => "按钮",
        ControlType.Edit => "编辑框",
        ControlType.Document => "文档",
        ControlType.MenuItem => "菜单项",
        ControlType.Menu => "菜单",
        ControlType.MenuBar => "菜单栏",
        ControlType.ListItem => "列表项",
        ControlType.List => "列表",
        ControlType.TreeItem => "树节点",
        ControlType.Tree => "树",
        ControlType.TabItem => "标签",
        ControlType.Tab => "标签页",
        ControlType.CheckBox => "复选框",
        ControlType.RadioButton => "单选框",
        ControlType.ComboBox => "组合框",
        ControlType.Hyperlink => "链接",
        ControlType.Image => "图像",
        ControlType.Text => "文本",
        ControlType.Window => "窗口",
        ControlType.Pane => "面板",
        ControlType.Group => "分组",
        ControlType.ToolBar => "工具栏",
        ControlType.TitleBar => "标题栏",
        ControlType.ScrollBar => "滚动条",
        ControlType.Slider => "滑块",
        ControlType.ProgressBar => "进度条",
        ControlType.StatusBar => "状态栏",
        ControlType.Table => "表格",
        ControlType.DataItem => "数据项",
        ControlType.DataGrid => "数据网格",
        ControlType.Custom => "自定义",
        ControlType.Header => "表头",
        ControlType.HeaderItem => "表头项",
        ControlType.SplitButton => "拆分按钮",
        ControlType.Spinner => "调节框",
        ControlType.ToolTip => "提示",
        ControlType.Separator => "分隔条",
        _ => type.ToString(),
    };
}
