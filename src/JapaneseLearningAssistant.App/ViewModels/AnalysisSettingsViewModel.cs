using System;
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

    [ObservableProperty]
    private string _selectedProvider;

    [ObservableProperty]
    private string _model;

    [ObservableProperty]
    private string _apiKey;

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
    }

    partial void OnSelectedProviderChanged(string value)
    {
        Model = GetModelForProvider(_currentConfig, value);
        ApiKey = GetApiKeyForProvider(_currentConfig, value);
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
            TargetStyle = "自然版"
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
        return AnalysisClientFactory.Create(config);
    }

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

    private static LocalAppConfig CloneConfig(LocalAppConfig config) => new()
    {
        AnalysisProvider = config.AnalysisProvider,
        GeminiApiKey = config.GeminiApiKey,
        GeminiModel = config.GeminiModel,
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
