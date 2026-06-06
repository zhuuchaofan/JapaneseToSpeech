using System.Text.Json;
using JapaneseLearningAssistant.Core.Services;

namespace JapaneseLearningAssistant.Core.Configuration;

public sealed class LocalAppConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string AnalysisProvider { get; set; } = "Gemini";
    public string GeminiApiKey { get; set; } = "";
    public string GeminiModel { get; set; } = "gemini-3.5-flash";
    public bool GeminiUseAgentPlatform { get; set; }
    public string GeminiProjectId { get; set; } = "";
    public string GeminiLocation { get; set; } = "global";
    public float GeminiTemperature { get; set; } = 0.2f;
    public float? GeminiTopP { get; set; }
    public float? GeminiTopK { get; set; }
    public int? GeminiMaxOutputTokens { get; set; }
    public string GeminiThinkingLevel { get; set; } = "MEDIUM";
    public string GeminiSafetyThreshold { get; set; } = "BLOCK_MEDIUM_AND_ABOVE";
    public string[] GeminiBlockedKeywords { get; set; } = [];
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
        config.GeminiUseAgentPlatform = FirstBool(config.GeminiUseAgentPlatform, Environment.GetEnvironmentVariable("GEMINI_USE_AGENT_PLATFORM"));
        config.GeminiProjectId = FirstNonEmpty(config.GeminiProjectId, Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT"));
        config.GeminiLocation = FirstNonEmpty(config.GeminiLocation, Environment.GetEnvironmentVariable("GOOGLE_CLOUD_LOCATION"), "global");
        config.GeminiTemperature = FirstFloat(config.GeminiTemperature, Environment.GetEnvironmentVariable("GEMINI_TEMPERATURE"));
        config.GeminiTopP = FirstNullableFloat(config.GeminiTopP, Environment.GetEnvironmentVariable("GEMINI_TOP_P"));
        config.GeminiTopK = FirstNullableFloat(config.GeminiTopK, Environment.GetEnvironmentVariable("GEMINI_TOP_K"));
        config.GeminiMaxOutputTokens = FirstNullableInt(config.GeminiMaxOutputTokens, Environment.GetEnvironmentVariable("GEMINI_MAX_OUTPUT_TOKENS"));
        config.GeminiThinkingLevel = FirstNonEmpty(config.GeminiThinkingLevel, Environment.GetEnvironmentVariable("GEMINI_THINKING_LEVEL"), "MEDIUM");
        config.GeminiSafetyThreshold = FirstNonEmpty(config.GeminiSafetyThreshold, Environment.GetEnvironmentVariable("GEMINI_SAFETY_THRESHOLD"), "BLOCK_MEDIUM_AND_ABOVE");
        config.GeminiBlockedKeywords = FirstStringArray(config.GeminiBlockedKeywords, Environment.GetEnvironmentVariable("GEMINI_BLOCKED_KEYWORDS"));
        config.OpenAiApiKey = FirstNonEmpty(config.OpenAiApiKey, Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        config.OpenAiModel = FirstNonEmpty(config.OpenAiModel, Environment.GetEnvironmentVariable("OPENAI_MODEL"), "gpt-4.1");
        config.DeepSeekApiKey = FirstNonEmpty(config.DeepSeekApiKey, Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY"));
        config.DeepSeekModel = FirstNonEmpty(config.DeepSeekModel, Environment.GetEnvironmentVariable("DEEPSEEK_MODEL"), "deepseek-v4-flash");
        config.MiMoApiKey = FirstNonEmpty(config.MiMoApiKey, Environment.GetEnvironmentVariable("MIMO_API_KEY"));
        config.MiMoModel = FirstNonEmpty(config.MiMoModel, Environment.GetEnvironmentVariable("MIMO_MODEL"), "mimo-v2.5-pro");
        config.GoogleTtsApiKey = FirstNonEmpty(config.GoogleTtsApiKey, Environment.GetEnvironmentVariable("GOOGLE_TTS_API_KEY"));
        config.GoogleTtsVoiceName = FirstNonEmpty(config.GoogleTtsVoiceName, Environment.GetEnvironmentVariable("GOOGLE_TTS_VOICE_NAME"), "ja-JP-Neural2-C");

        AppLogger.Info($"Configuration loaded. AnalysisProvider={config.AnalysisProvider}, GeminiModel={config.GeminiModel}, GeminiUseAgentPlatform={config.GeminiUseAgentPlatform}, GeminiLocation={config.GeminiLocation}, OpenAiModel={config.OpenAiModel}, DeepSeekModel={config.DeepSeekModel}, MiMoModel={config.MiMoModel}, GoogleTtsVoiceName={config.GoogleTtsVoiceName}, HasGeminiApiKey={!string.IsNullOrWhiteSpace(config.GeminiApiKey)}, HasOpenAiApiKey={!string.IsNullOrWhiteSpace(config.OpenAiApiKey)}, HasDeepSeekApiKey={!string.IsNullOrWhiteSpace(config.DeepSeekApiKey)}, HasMiMoApiKey={!string.IsNullOrWhiteSpace(config.MiMoApiKey)}, HasGoogleTtsApiKey={!string.IsNullOrWhiteSpace(config.GoogleTtsApiKey)}.");

        return config;
    }

    public static string Save(LocalAppConfig config)
    {
        var path = FindLocalConfigPath() ?? Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
        AppLogger.Info($"Local configuration saved. Path={path}, AnalysisProvider={config.AnalysisProvider}.");
        return path;
    }

    public string GetAnalysisModel()
    {
        return NormalizeAnalysisProvider(AnalysisProvider) switch
        {
            "openai" => OpenAiModel,
            "deepseek" => DeepSeekModel,
            "mimo" => MiMoModel,
            _ => GeminiModel
        };
    }

    public static string NormalizeAnalysisProvider(string value)
    {
        return value.Trim()
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .ToLowerInvariant();
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
        return JsonSerializer.Deserialize<LocalAppConfig>(json, JsonOptions)
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

    private static bool FirstBool(bool currentValue, string? environmentValue)
    {
        return bool.TryParse(environmentValue, out var parsed) ? parsed : currentValue;
    }

    private static float FirstFloat(float currentValue, string? environmentValue)
    {
        return float.TryParse(environmentValue, out var parsed) ? parsed : currentValue;
    }

    private static float? FirstNullableFloat(float? currentValue, string? environmentValue)
    {
        return float.TryParse(environmentValue, out var parsed) ? parsed : currentValue;
    }

    private static int? FirstNullableInt(int? currentValue, string? environmentValue)
    {
        return int.TryParse(environmentValue, out var parsed) ? parsed : currentValue;
    }

    private static string[] FirstStringArray(string[] currentValue, string? environmentValue)
    {
        if (string.IsNullOrWhiteSpace(environmentValue))
        {
            return currentValue;
        }

        return environmentValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
