namespace CaptureDemo.Snip;

internal enum SnipMode
{
    Fullscreen,
    Rectangle,
    Window,
    Long,
    Element,
    Freeform,
}

internal enum SnipFollow
{
    None,
    Long,
    Element,
    Settings,
}

internal readonly record struct SnipResult(CaptureOutcome Outcome, SnipFollow Follow, Rectangle? Preset);

internal enum AnnoTool
{
    Pen,
    Arrow,
    Highlight,
    Mosaic,
}
