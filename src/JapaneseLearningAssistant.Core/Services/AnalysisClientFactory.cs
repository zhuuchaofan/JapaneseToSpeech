using JapaneseLearningAssistant.Core.Configuration;

namespace JapaneseLearningAssistant.Core.Services;

public static class AnalysisClientFactory
{
    public static IAnalysisClient Create(LocalAppConfig config)
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120)
        };

        return NormalizeProvider(config.AnalysisProvider) switch
        {
            "openai" => new OpenAiResponsesAnalysisClient(httpClient, config.OpenAiApiKey, config.OpenAiModel),
            "deepseek" => new OpenAiCompatibleChatAnalysisClient(
                httpClient,
                "DeepSeek",
                "https://api.deepseek.com",
                config.DeepSeekApiKey,
                config.DeepSeekModel),
            "mimo" => new OpenAiCompatibleChatAnalysisClient(
                httpClient,
                "MiMo",
                "https://api.xiaomimimo.com/v1",
                config.MiMoApiKey,
                config.MiMoModel,
                apiKeyHeaderName: "api-key",
                usesMaxCompletionTokens: true),
            _ => new GoogleGeminiClient(httpClient, config.GeminiApiKey, config.GeminiModel)
        };
    }

    private static string NormalizeProvider(string value)
    {
        return value.Trim().Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
    }
}
