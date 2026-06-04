using System.Text.Json;

namespace JapaneseLearningAssistant.Core.Configuration;

public sealed class LocalAppConfig
{
    public string GeminiApiKey { get; set; } = "";
    public string GoogleTtsApiKey { get; set; } = "";
    public string GeminiModel { get; set; } = "gemini-2.5-flash";
    public string GoogleTtsVoiceName { get; set; } = "ja-JP-Neural2-B";

    public static LocalAppConfig Load()
    {
        var config = LoadFromLocalFile();

        config.GeminiApiKey = FirstNonEmpty(config.GeminiApiKey, Environment.GetEnvironmentVariable("GEMINI_API_KEY"));
        config.GoogleTtsApiKey = FirstNonEmpty(config.GoogleTtsApiKey, Environment.GetEnvironmentVariable("GOOGLE_TTS_API_KEY"));
        config.GeminiModel = FirstNonEmpty(config.GeminiModel, Environment.GetEnvironmentVariable("GEMINI_MODEL"), "gemini-2.5-flash");
        config.GoogleTtsVoiceName = FirstNonEmpty(config.GoogleTtsVoiceName, Environment.GetEnvironmentVariable("GOOGLE_TTS_VOICE_NAME"), "ja-JP-Neural2-B");

        return config;
    }

    private static LocalAppConfig LoadFromLocalFile()
    {
        var path = FindLocalConfigPath();
        if (path is null)
        {
            return new LocalAppConfig();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LocalAppConfig>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? new LocalAppConfig();
    }

    private static string? FindLocalConfigPath()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var path = Path.Combine(current, "appsettings.Local.json");
            if (File.Exists(path))
            {
                return path;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        return null;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }
}
