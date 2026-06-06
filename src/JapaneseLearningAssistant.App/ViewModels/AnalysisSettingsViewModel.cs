using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JapaneseLearningAssistant.Core.Configuration;
using JapaneseLearningAssistant.Core.Models;
using JapaneseLearningAssistant.Core.Services;

namespace JapaneseLearningAssistant.App.ViewModels;

public sealed partial class AnalysisSettingsViewModel : ObservableObject
{
    private LocalAppConfig _currentConfig;
    private readonly Action<LocalAppConfig, IAnalysisClient> _onSettingsSaved;
    private readonly Action<string> _onStatusTextChanged;
    private readonly Func<string, Func<CancellationToken, Task>, Task> _runBusyAsync;

    public string[] AnalysisProviders { get; } = ["Gemini", "OpenAI", "DeepSeek", "MiMo"];
    public string[] GeminiThinkingLevels { get; } =
    [
        "默认：跟随模型",
        "极低：最快响应",
        "低：简单分析",
        "中：推荐",
        "高：更细致"
    ];

    public string[] GeminiSafetyThresholds { get; } =
    [
        "关闭：不额外拦截",
        "宽松：仅拦截极少内容",
        "轻度：只拦截高风险",
        "标准：拦截中高风险",
        "严格：低风险也拦截"
    ];

    [ObservableProperty]
    private string _selectedProvider;

    [ObservableProperty]
    private string _model;

    [ObservableProperty]
    private string _apiKey;

    [ObservableProperty]
    private bool _isGeminiSelected;

    [ObservableProperty]
    private bool _geminiUseAgentPlatform;

    [ObservableProperty]
    private string _geminiProjectId = "";

    [ObservableProperty]
    private string _geminiLocation = "global";

    [ObservableProperty]
    private string _geminiTemperature = "0.2";

    [ObservableProperty]
    private string _geminiTopP = "";

    [ObservableProperty]
    private string _geminiTopK = "";

    [ObservableProperty]
    private string _geminiMaxOutputTokens = "";

    [ObservableProperty]
    private string _geminiThinkingLevel = "中：推荐";

    [ObservableProperty]
    private string _geminiSafetyThreshold = "标准：拦截中高风险";

    [ObservableProperty]
    private string _geminiBlockedKeywords = "";

    public AnalysisSettingsViewModel(
        LocalAppConfig currentConfig,
        Action<LocalAppConfig, IAnalysisClient> onSettingsSaved,
        Action<string> onStatusTextChanged,
        Func<string, Func<CancellationToken, Task>, Task> runBusyAsync)
    {
        _currentConfig = currentConfig;
        _onSettingsSaved = onSettingsSaved;
        _onStatusTextChanged = onStatusTextChanged;
        _runBusyAsync = runBusyAsync;

        _selectedProvider = NormalizeProviderDisplayName(_currentConfig.AnalysisProvider);
        _model = GetModelForProvider(_currentConfig, _selectedProvider);
        _apiKey = GetApiKeyForProvider(_currentConfig, _selectedProvider);
        LoadGeminiSettings(_currentConfig);
        _isGeminiSelected = IsGeminiProvider(_selectedProvider);
    }

    partial void OnSelectedProviderChanged(string value)
    {
        Model = GetModelForProvider(_currentConfig, value);
        ApiKey = GetApiKeyForProvider(_currentConfig, value);
        IsGeminiSelected = IsGeminiProvider(value);
        _onStatusTextChanged?.Invoke($"已切换分析供应商为 {NormalizeProviderDisplayName(value)}。");
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        var client = CreateClient();
        if (_runBusyAsync != null)
        {
            await _runBusyAsync($"正在测试 {client.ProviderName} 连接...", async cancellationToken =>
            {
                await RunTestAsync(client, cancellationToken);
            });
        }
        else
        {
            await RunTestAsync(client, CancellationToken.None);
        }
    }

    private async Task RunTestAsync(IAnalysisClient client, CancellationToken cancellationToken)
    {
        AppLogger.Info($"Analysis settings test started. Provider={SelectedProvider}, Model={Model}.");
        var result = await client.AnalyzeAsync(new AnalyzeTextRequest
        {
            Text = "今天也一起学习日语。",
            LanguageMode = InputLanguageMode.Japanese,
            Scenario = "日常学习",
            TargetStyle = "润色：地道自然"
        }, cancellationToken);

        if (string.IsNullOrWhiteSpace(result.NaturalJapanese)
            && string.IsNullOrWhiteSpace(result.CorrectedJapanese)
            && string.IsNullOrWhiteSpace(result.ReadingOptimizedJapanese))
        {
            throw new InvalidOperationException($"{client.ProviderName} 已返回结果，但没有可用的日语文本。");
        }

        AppLogger.Info($"Analysis settings test succeeded. Provider={client.ProviderName}, Issues={result.Issues.Count}.");
        _onStatusTextChanged?.Invoke($"{client.ProviderName} 测试成功。");
    }

    [RelayCommand]
    private void SaveSettings()
    {
        var config = CloneConfig(_currentConfig);
        config.AnalysisProvider = NormalizeProviderDisplayName(SelectedProvider);
        SetProviderOverrides(config, SelectedProvider, Model, ApiKey);
        ApplyGeminiSettings(config);

        var path = LocalAppConfig.Save(config);
        _currentConfig = config;
        
        var newClient = AnalysisClientFactory.Create(config);
        _onSettingsSaved?.Invoke(config, newClient);
        _onStatusTextChanged?.Invoke($"AI 设置已保存到 {path}。");
    }

    public IAnalysisClient CreateClient()
    {
        var config = CloneConfig(_currentConfig);
        config.AnalysisProvider = SelectedProvider;
        SetProviderOverrides(config, SelectedProvider, Model, ApiKey);
        ApplyGeminiSettings(config);
        return AnalysisClientFactory.Create(config);
    }

    private static bool IsGeminiProvider(string provider) => NormalizeProviderDisplayName(provider) == "Gemini";

    private static string NormalizeProviderDisplayName(string value) => value.Trim().ToLowerInvariant() switch
    {
        "openai" or "open ai" => "OpenAI",
        "deepseek" or "deep seek" => "DeepSeek",
        "mimo" or "mi mo" => "MiMo",
        _ => "Gemini"
    };

    private static string GetModelForProvider(LocalAppConfig config, string provider) => NormalizeProviderDisplayName(provider) switch
    {
        "OpenAI" => config.OpenAiModel,
        "DeepSeek" => config.DeepSeekModel,
        "MiMo" => config.MiMoModel,
        _ => config.GeminiModel
    };

    private static string GetApiKeyForProvider(LocalAppConfig config, string provider) => NormalizeProviderDisplayName(provider) switch
    {
        "OpenAI" => config.OpenAiApiKey,
        "DeepSeek" => config.DeepSeekApiKey,
        "MiMo" => config.MiMoApiKey,
        _ => config.GeminiApiKey
    };

    private static void SetProviderOverrides(LocalAppConfig config, string provider, string model, string apiKey)
    {
        switch (NormalizeProviderDisplayName(provider))
        {
            case "OpenAI":
                config.OpenAiModel = model;
                config.OpenAiApiKey = apiKey;
                break;
            case "DeepSeek":
                config.DeepSeekModel = model;
                config.DeepSeekApiKey = apiKey;
                break;
            case "MiMo":
                config.MiMoModel = model;
                config.MiMoApiKey = apiKey;
                break;
            default:
                config.GeminiModel = model;
                config.GeminiApiKey = apiKey;
                break;
        }
    }

    private void LoadGeminiSettings(LocalAppConfig config)
    {
        GeminiUseAgentPlatform = config.GeminiUseAgentPlatform;
        GeminiProjectId = config.GeminiProjectId;
        GeminiLocation = config.GeminiLocation;
        GeminiTemperature = FormatNullableFloat(config.GeminiTemperature);
        GeminiTopP = FormatNullableFloat(config.GeminiTopP);
        GeminiTopK = FormatNullableFloat(config.GeminiTopK);
        GeminiMaxOutputTokens = config.GeminiMaxOutputTokens?.ToString(CultureInfo.InvariantCulture) ?? "";
        GeminiThinkingLevel = ToThinkingDisplayValue(config.GeminiThinkingLevel);
        GeminiSafetyThreshold = ToSafetyDisplayValue(config.GeminiSafetyThreshold);
        GeminiBlockedKeywords = string.Join(", ", config.GeminiBlockedKeywords);
    }

    private void ApplyGeminiSettings(LocalAppConfig config)
    {
        config.GeminiUseAgentPlatform = GeminiUseAgentPlatform;
        config.GeminiProjectId = GeminiProjectId.Trim();
        config.GeminiLocation = string.IsNullOrWhiteSpace(GeminiLocation) ? "global" : GeminiLocation.Trim();
        config.GeminiTemperature = ParseFloat(GeminiTemperature, 0.2f, nameof(GeminiTemperature));
        config.GeminiTopP = ParseNullableFloat(GeminiTopP, nameof(GeminiTopP));
        config.GeminiTopK = ParseNullableFloat(GeminiTopK, nameof(GeminiTopK));
        config.GeminiMaxOutputTokens = ParseNullableInt(GeminiMaxOutputTokens, nameof(GeminiMaxOutputTokens));
        config.GeminiThinkingLevel = ToThinkingApiValue(GeminiThinkingLevel);
        config.GeminiSafetyThreshold = ToSafetyApiValue(GeminiSafetyThreshold);
        config.GeminiBlockedKeywords = GeminiBlockedKeywords
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string ToThinkingDisplayValue(string value)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "DEFAULT" => "默认：跟随模型",
            "MINIMAL" => "极低：最快响应",
            "LOW" => "低：简单分析",
            "HIGH" => "高：更细致",
            _ => "中：推荐"
        };
    }

    private static string ToThinkingApiValue(string value)
    {
        return value.Trim() switch
        {
            "默认：跟随模型" => "DEFAULT",
            "极低：最快响应" => "MINIMAL",
            "低：简单分析" => "LOW",
            "高：更细致" => "HIGH",
            _ => "MEDIUM"
        };
    }

    private static string ToSafetyDisplayValue(string value)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "OFF" => "关闭：不额外拦截",
            "BLOCK_NONE" => "宽松：仅拦截极少内容",
            "BLOCK_ONLY_HIGH" => "轻度：只拦截高风险",
            "BLOCK_LOW_AND_ABOVE" => "严格：低风险也拦截",
            _ => "标准：拦截中高风险"
        };
    }

    private static string ToSafetyApiValue(string value)
    {
        return value.Trim() switch
        {
            "关闭：不额外拦截" => "OFF",
            "宽松：仅拦截极少内容" => "BLOCK_NONE",
            "轻度：只拦截高风险" => "BLOCK_ONLY_HIGH",
            "严格：低风险也拦截" => "BLOCK_LOW_AND_ABOVE",
            _ => "BLOCK_MEDIUM_AND_ABOVE"
        };
    }

    private static string FormatNullableFloat(float? value)
    {
        return value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";
    }

    private static float ParseFloat(string value, float fallback, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"{fieldName} 必须是数字，例如 0.2。");
    }

    private static float? ParseNullableFloat(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"{fieldName} 必须是数字，留空表示使用模型默认值。");
    }

    private static int? ParseNullableInt(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"{fieldName} 必须是整数，留空表示使用模型默认值。");
    }

    private static LocalAppConfig CloneConfig(LocalAppConfig config) => new()
    {
        AnalysisProvider = config.AnalysisProvider,
        GeminiApiKey = config.GeminiApiKey,
        GeminiModel = config.GeminiModel,
        GeminiUseAgentPlatform = config.GeminiUseAgentPlatform,
        GeminiProjectId = config.GeminiProjectId,
        GeminiLocation = config.GeminiLocation,
        GeminiTemperature = config.GeminiTemperature,
        GeminiTopP = config.GeminiTopP,
        GeminiTopK = config.GeminiTopK,
        GeminiMaxOutputTokens = config.GeminiMaxOutputTokens,
        GeminiThinkingLevel = config.GeminiThinkingLevel,
        GeminiSafetyThreshold = config.GeminiSafetyThreshold,
        GeminiBlockedKeywords = [.. config.GeminiBlockedKeywords],
        OpenAiApiKey = config.OpenAiApiKey,
        OpenAiModel = config.OpenAiModel,
        DeepSeekApiKey = config.DeepSeekApiKey,
        DeepSeekModel = config.DeepSeekModel,
        MiMoApiKey = config.MiMoApiKey,
        MiMoModel = config.MiMoModel,
        GoogleTtsApiKey = config.GoogleTtsApiKey,
        GoogleTtsVoiceName = config.GoogleTtsVoiceName
    };
}
