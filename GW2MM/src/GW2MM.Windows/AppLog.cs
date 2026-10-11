namespace GW2MM.Windows;

/// <summary>Small append-only log (GW2MM.log next to the data) for problems that should not be lost.</summary>
internal static class AppLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void Init(string dataDir) => _path = Path.Combine(dataDir, "GW2MM.log");

    public static void Write(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        if (_path is null) return;
        try
        {
            lock (Gate)
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > 1_000_000) File.Move(_path, _path + ".old", overwrite: true);
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
