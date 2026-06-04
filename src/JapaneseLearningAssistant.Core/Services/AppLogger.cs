namespace JapaneseLearningAssistant.Core.Services;

public static class AppLogger
{
    private static readonly object SyncRoot = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Warning(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(Exception exception, string message)
    {
        Write("ERROR", $"{message}{Environment.NewLine}{exception}");
    }

    private static void Write(string level, string message)
    {
        try
        {
            var logDirectory = GetLogDirectory();
            Directory.CreateDirectory(logDirectory);

            var now = DateTimeOffset.Now;
            var logPath = Path.Combine(logDirectory, $"{now:yyyyMMdd}.log");
            var line = $"{now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}";

            lock (SyncRoot)
            {
                File.AppendAllText(logPath, line);
            }
        }
        catch
        {
            // Logging must never break the app.
        }
    }

    private static string GetLogDirectory()
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = AppContext.BaseDirectory;
        }

        return Path.Combine(baseDirectory, "JapaneseLearningAssistant", "logs");
    }
}
