namespace CaptureDemo.Capture;

internal enum CaptureOutcomeKind
{
    Saved,
    Cancelled,
    Failed,
    OpenSettings,
}

internal sealed record CaptureOutcome(CaptureOutcomeKind Kind, string? FilePath, string Message)
{
    public static CaptureOutcome Saved(string path, string message) => new(CaptureOutcomeKind.Saved, path, message);

    public static CaptureOutcome Cancelled(string message = "已取消。") => new(CaptureOutcomeKind.Cancelled, null, message);

    public static CaptureOutcome Failed(string message) => new(CaptureOutcomeKind.Failed, null, message);

    public static CaptureOutcome OpenSettings() => new(CaptureOutcomeKind.OpenSettings, null, "打开设置。");
}
