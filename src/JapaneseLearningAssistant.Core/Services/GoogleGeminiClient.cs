using Google.GenAI;
using Google.GenAI.Types;
using JapaneseLearningAssistant.Core.Models;

namespace JapaneseLearningAssistant.Core.Services;

public sealed class GoogleGeminiClient : IAnalysisClient
{
    private readonly string _apiKey;
    private readonly string _model;

    public GoogleGeminiClient(HttpClient httpClient, string apiKey, string model = "gemini-3.5-flash")
    {
        _ = httpClient;
        _apiKey = apiKey;
        _model = model;
    }

    public string ProviderName => "Gemini";

    public async Task<JapaneseAnalysisResult> AnalyzeAsync(AnalyzeTextRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("缺少 Gemini API Key。请在 appsettings.Local.json 的 geminiApiKey 中配置，或设置 GEMINI_API_KEY 环境变量。");
        }

        try
        {
            var prompt = JapaneseAnalysisPromptBuilder.Build(request);
            var client = new Client(apiKey: _apiKey);
            var config = new GenerateContentConfig
            {
                Temperature = 0.2f,
                ResponseMimeType = "application/json",
                ThinkingConfig = new ThinkingConfig
                {
                    ThinkingLevel = "MEDIUM"
                }
            };

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
}
