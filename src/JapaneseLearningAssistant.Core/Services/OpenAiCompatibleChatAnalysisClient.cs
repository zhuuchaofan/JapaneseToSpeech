using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JapaneseLearningAssistant.Core.Models;

namespace JapaneseLearningAssistant.Core.Services;

public sealed class OpenAiCompatibleChatAnalysisClient : IAnalysisClient
{
    private readonly HttpClient _httpClient;
    private readonly string _providerName;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _apiKeyHeaderName;
    private readonly string _model;
    private readonly bool _usesMaxCompletionTokens;

    public OpenAiCompatibleChatAnalysisClient(
        HttpClient httpClient,
        string providerName,
        string baseUrl,
        string apiKey,
        string model,
        string apiKeyHeaderName = "Authorization",
        bool usesMaxCompletionTokens = false)
    {
        _httpClient = httpClient;
        _providerName = providerName;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _apiKeyHeaderName = apiKeyHeaderName;
        _model = model;
        _usesMaxCompletionTokens = usesMaxCompletionTokens;
    }

    public string ProviderName => _providerName;

    public async Task<JapaneseAnalysisResult> AnalyzeAsync(AnalyzeTextRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException($"缺少 {_providerName} API Key。请在 appsettings.Local.json 中配置，或设置对应环境变量。");
        }

        var prompt = JapaneseAnalysisPromptBuilder.Build(request);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions");
        ApplyApiKeyHeader(httpRequest);
        httpRequest.Content = JsonContent.Create(BuildRequestBody(prompt));

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{_providerName} API 调用失败：{(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
        }

        var generated = ExtractMessageContent(body);
        return AnalysisJsonParser.Parse(generated, _providerName);
    }

    private void ApplyApiKeyHeader(HttpRequestMessage request)
    {
        if (string.Equals(_apiKeyHeaderName, "Authorization", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            return;
        }

        request.Headers.TryAddWithoutValidation(_apiKeyHeaderName, _apiKey);
    }

    private object BuildRequestBody(string prompt)
    {
        var messages = new[]
        {
            new
            {
                role = "system",
                content = "你是严格的 JSON API。你必须只输出一个合法 JSON 对象，不要输出 Markdown 或额外解释。"
            },
            new
            {
                role = "user",
                content = prompt
            }
        };

        if (_usesMaxCompletionTokens)
        {
            return new
            {
                model = _model,
                messages,
                temperature = 0.2,
                stream = false,
                max_completion_tokens = 4096,
                response_format = new { type = "json_object" }
            };
        }

        return new
        {
            model = _model,
            messages,
            temperature = 0.2,
            stream = false,
            max_tokens = 4096,
            response_format = new { type = "json_object" }
        };
    }

    private static string ExtractMessageContent(string body)
    {
        using var document = JsonDocument.Parse(body);
        var choices = document.RootElement.GetProperty("choices");
        if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Chat Completions API 没有返回 choices。");
        }

        var first = choices[0];
        if (!first.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Chat Completions API 没有返回 message.content。");
        }

        return content.GetString() ?? "";
    }
}
