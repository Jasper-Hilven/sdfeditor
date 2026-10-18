namespace SDFPipeline.Rendering;

public static class ChromeLocator
{
    /// <summary>Set this environment variable to the Chrome or Chromium executable when it is not in a standard place.</summary>
    public const string EnvironmentVariable = "SDF_CHROME";

    private static readonly string[] StandardPaths =
    [
        "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
        "/Applications/Chromium.app/Contents/MacOS/Chromium",
        "/usr/bin/google-chrome",
        "/usr/bin/google-chrome-stable",
        "/usr/bin/chromium",
        "/usr/bin/chromium-browser",
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    ];

    public static string Find() =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } configured
            ? File.Exists(configured) ? configured : throw new FileNotFoundException($"{EnvironmentVariable} points to {configured}, which does not exist")
            : StandardPaths.FirstOrDefault(File.Exists)
              ?? throw new FileNotFoundException($"Chrome was not found; install it or set {EnvironmentVariable} to its executable");
}
