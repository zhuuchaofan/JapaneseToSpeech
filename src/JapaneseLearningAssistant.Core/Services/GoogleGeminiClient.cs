using Google.GenAI;
using Google.GenAI.Types;
using JapaneseLearningAssistant.Core.Configuration;
using JapaneseLearningAssistant.Core.Models;

namespace JapaneseLearningAssistant.Core.Services;

public sealed class GoogleGeminiClient : IAnalysisClient
{
    private readonly string _apiKey;
    private readonly string _model;
    private readonly bool _useAgentPlatform;
    private readonly string _projectId;
    private readonly string _location;
    private readonly float _temperature;
    private readonly float? _topP;
    private readonly float? _topK;
    private readonly int? _maxOutputTokens;
    private readonly string _thinkingLevel;
    private readonly string _safetyThreshold;
    private readonly string[] _blockedKeywords;

    public GoogleGeminiClient(HttpClient httpClient, LocalAppConfig config)
    {
        _ = httpClient;
        _apiKey = config.GeminiApiKey;
        _model = config.GeminiModel;
        _useAgentPlatform = config.GeminiUseAgentPlatform;
        _projectId = config.GeminiProjectId;
        _location = config.GeminiLocation;
        _temperature = config.GeminiTemperature;
        _topP = config.GeminiTopP;
        _topK = config.GeminiTopK;
        _maxOutputTokens = config.GeminiMaxOutputTokens;
        _thinkingLevel = config.GeminiThinkingLevel.Trim().ToUpperInvariant();
        _safetyThreshold = config.GeminiSafetyThreshold.Trim().ToUpperInvariant();
        _blockedKeywords = config.GeminiBlockedKeywords
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Select(keyword => keyword.Trim())
            .ToArray();
    }

    public string ProviderName => "Gemini";

    public async Task<JapaneseAnalysisResult> AnalyzeAsync(AnalyzeTextRequest request, CancellationToken cancellationToken)
    {
        if (_useAgentPlatform)
        {
            if (string.IsNullOrWhiteSpace(_projectId))
            {
                throw new InvalidOperationException("缺少 Google Cloud Project ID。请在 appsettings.Local.json 的 geminiProjectId 中配置，或设置 GOOGLE_CLOUD_PROJECT 环境变量。");
            }
        }
        else if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("缺少 Gemini API Key。请在 appsettings.Local.json 的 geminiApiKey 中配置，或设置 GEMINI_API_KEY 环境变量。");
        }

        var blockedKeyword = _blockedKeywords.FirstOrDefault(keyword =>
            request.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        if (blockedKeyword is not null)
        {
            throw new InvalidOperationException($"输入内容命中了本地关键词过滤：{blockedKeyword}");
        }

        try
        {
            var prompt = JapaneseAnalysisPromptBuilder.Build(request);
            using var client = CreateClient();
            var config = BuildGenerateContentConfig();

            var response = await client.Models.GenerateContentAsync(
                model: _model,
                contents: prompt,
                config: config,
                cancellationToken: cancellationToken);

            var generated = response.Candidates
                ?.FirstOrDefault()
                ?.Content
                ?.Parts
                ?.FirstOrDefault()
                ?.Text;

            return AnalysisJsonParser.Parse(generated ?? "", ProviderName);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(GoogleApiErrorFormatter.FormatSdkException("Gemini API", ex), ex);
        }
    }

    private Client CreateClient()
    {
        if (_useAgentPlatform)
        {
            return new Client(enterprise: true, project: _projectId, location: _location);
        }

        return new Client(apiKey: _apiKey);
    }

    private GenerateContentConfig BuildGenerateContentConfig()
    {
        var config = new GenerateContentConfig
        {
            Temperature = _temperature,
            ResponseMimeType = "application/json",
            SafetySettings = BuildSafetySettings()
        };

        if (_topP.HasValue)
        {
            config.TopP = _topP.Value;
        }

        if (_topK.HasValue)
        {
            config.TopK = _topK.Value;
        }

        if (_maxOutputTokens.HasValue)
        {
            config.MaxOutputTokens = _maxOutputTokens.Value;
        }

        if (!string.Equals(_thinkingLevel, "DEFAULT", StringComparison.OrdinalIgnoreCase))
        {
            config.ThinkingConfig = new ThinkingConfig
            {
                ThinkingLevel = _thinkingLevel
            };
        }

        return config;
    }

    private List<SafetySetting> BuildSafetySettings()
    {
        return
        [
            new SafetySetting { Category = "HARM_CATEGORY_DANGEROUS_CONTENT", Threshold = _safetyThreshold },
            new SafetySetting { Category = "HARM_CATEGORY_HARASSMENT", Threshold = _safetyThreshold },
            new SafetySetting { Category = "HARM_CATEGORY_HATE_SPEECH", Threshold = _safetyThreshold },
            new SafetySetting { Category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", Threshold = _safetyThreshold }
        ];
    }
}
