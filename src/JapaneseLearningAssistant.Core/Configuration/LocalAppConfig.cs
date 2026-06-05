using System.Text.Json;
using JapaneseLearningAssistant.Core.Services;

namespace JapaneseLearningAssistant.Core.Configuration;

public sealed class LocalAppConfig
{
    public string AnalysisProvider { get; set; } = "Gemini";
    public string GeminiApiKey { get; set; } = "";
    public string GeminiModel { get; set; } = "gemini-3.5-flash";
    public string OpenAiApiKey { get; set; } = "";
    public string OpenAiModel { get; set; } = "gpt-4.1";
    public string DeepSeekApiKey { get; set; } = "";
    public string DeepSeekModel { get; set; } = "deepseek-v4-flash";
    public string MiMoApiKey { get; set; } = "";
    public string MiMoModel { get; set; } = "mimo-v2.5-pro";
    public string GoogleTtsApiKey { get; set; } = "";
    public string GoogleTtsVoiceName { get; set; } = "ja-JP-Neural2-C";

    public static LocalAppConfig Load()
    {
        AppLogger.Info("Loading local app configuration.");
        var config = LoadFromLocalFile();

        config.AnalysisProvider = FirstNonEmpty(config.AnalysisProvider, Environment.GetEnvironmentVariable("ANALYSIS_PROVIDER"), "Gemini");
        config.GeminiApiKey = FirstNonEmpty(config.GeminiApiKey, Environment.GetEnvironmentVariable("GEMINI_API_KEY"));
        config.GeminiModel = FirstNonEmpty(config.GeminiModel, Environment.GetEnvironmentVariable("GEMINI_MODEL"), "gemini-3.5-flash");
        config.OpenAiApiKey = FirstNonEmpty(config.OpenAiApiKey, Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        config.OpenAiModel = FirstNonEmpty(config.OpenAiModel, Environment.GetEnvironmentVariable("OPENAI_MODEL"), "gpt-4.1");
        config.DeepSeekApiKey = FirstNonEmpty(config.DeepSeekApiKey, Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY"));
        config.DeepSeekModel = FirstNonEmpty(config.DeepSeekModel, Environment.GetEnvironmentVariable("DEEPSEEK_MODEL"), "deepseek-v4-flash");
        config.MiMoApiKey = FirstNonEmpty(config.MiMoApiKey, Environment.GetEnvironmentVariable("MIMO_API_KEY"));
        config.MiMoModel = FirstNonEmpty(config.MiMoModel, Environment.GetEnvironmentVariable("MIMO_MODEL"), "mimo-v2.5-pro");
        config.GoogleTtsApiKey = FirstNonEmpty(config.GoogleTtsApiKey, Environment.GetEnvironmentVariable("GOOGLE_TTS_API_KEY"));
        config.GoogleTtsVoiceName = FirstNonEmpty(config.GoogleTtsVoiceName, Environment.GetEnvironmentVariable("GOOGLE_TTS_VOICE_NAME"), "ja-JP-Neural2-C");

        AppLogger.Info($"Configuration loaded. AnalysisProvider={config.AnalysisProvider}, GeminiModel={config.GeminiModel}, OpenAiModel={config.OpenAiModel}, DeepSeekModel={config.DeepSeekModel}, MiMoModel={config.MiMoModel}, GoogleTtsVoiceName={config.GoogleTtsVoiceName}, HasGeminiApiKey={!string.IsNullOrWhiteSpace(config.GeminiApiKey)}, HasOpenAiApiKey={!string.IsNullOrWhiteSpace(config.OpenAiApiKey)}, HasDeepSeekApiKey={!string.IsNullOrWhiteSpace(config.DeepSeekApiKey)}, HasMiMoApiKey={!string.IsNullOrWhiteSpace(config.MiMoApiKey)}, HasGoogleTtsApiKey={!string.IsNullOrWhiteSpace(config.GoogleTtsApiKey)}.");

        return config;
    }

    private static LocalAppConfig LoadFromLocalFile()
    {
        var path = FindLocalConfigPath();
        if (path is null)
        {
            AppLogger.Warning("appsettings.Local.json was not found. Falling back to environment variables and defaults.");
            return new LocalAppConfig();
        }

        AppLogger.Info($"Using local configuration file: {path}");
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
