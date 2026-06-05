using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JapaneseLearningAssistant.Core.Models;

namespace JapaneseLearningAssistant.Core.Services;

public sealed class OpenAiResponsesAnalysisClient : IAnalysisClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    public OpenAiResponsesAnalysisClient(HttpClient httpClient, string apiKey, string model = "gpt-4.1")
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _model = model;
    }

    public string ProviderName => "OpenAI";

    public async Task<JapaneseAnalysisResult> AnalyzeAsync(AnalyzeTextRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("缺少 OpenAI API Key。请在 appsettings.Local.json 的 openAiApiKey 中配置，或设置 OPENAI_API_KEY 环境变量。");
        }

        var prompt = JapaneseAnalysisPromptBuilder.Build(request);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        httpRequest.Content = JsonContent.Create(new
        {
            model = _model,
            input = prompt,
            temperature = 0.2,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "japanese_analysis_result",
                    strict = true,
                    schema = JapaneseAnalysisJsonSchema.Create()
                }
            }
        });

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI API 调用失败：{(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
        }

        var generated = ExtractResponseText(body);
        return AnalysisJsonParser.Parse(generated, ProviderName);
    }

    private static string ExtractResponseText(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("output_text", out var outputText)
            && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? "";
        }

        if (document.RootElement.TryGetProperty("output", out var output)
            && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var outputItem in output.EnumerateArray())
            {
                if (!outputItem.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var contentItem in content.EnumerateArray())
                {
                    if (contentItem.TryGetProperty("type", out var type)
                        && type.GetString() == "output_text"
                        && contentItem.TryGetProperty("text", out var text)
                        && text.ValueKind == JsonValueKind.String)
                    {
                        return text.GetString() ?? "";
                    }
                }
            }
        }

        throw new InvalidOperationException("OpenAI API 没有返回可解析的 output_text。");
    }
}
