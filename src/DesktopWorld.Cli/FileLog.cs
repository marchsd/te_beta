namespace DesktopWorld.Cli;

/// <summary>Minimal thread-safe file log at %LOCALAPPDATA%\DesktopWorld\logs\deskworld-yyyyMMdd.log. Never throws.</summary>
internal static class FileLog
{
    private static readonly object Gate = new();
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopWorld", "logs");

    public static void Write(string component, string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(Path.Combine(Directory, $"deskworld-{DateTime.Now:yyyyMMdd}.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} [{component}] {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the app */ }
    }
}
