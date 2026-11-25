namespace BAAxis;

internal static class RestartDetection
{
    // Stable text inside the restart button in the supplied 1920×1080 menu.
    public static readonly Rectangle Region = new(800, 780, 100, 55);

    public static Bitmap CreateSample()
    {
        using var stream = typeof(RestartDetection).Assembly.GetManifestResourceStream("BAAxis.Assets.restart-menu.png")
            ?? throw new InvalidOperationException("程序缺少内置重开菜单样本。");
        using var screenshot = new Bitmap(stream);
        return screenshot.Clone(Region, screenshot.PixelFormat);
    }
}
