using System.Text.Json;
using Google.GenAI;
using Google.GenAI.Types;
using JapaneseLearningAssistant.Core.Models;

namespace JapaneseLearningAssistant.Core.Services;

public sealed class GoogleGeminiClient : IGeminiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    public GoogleGeminiClient(HttpClient httpClient, string apiKey, string model = "gemini-3.5-flash")
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _model = model;
    }

    public async Task<JapaneseAnalysisResult> AnalyzeAsync(AnalyzeTextRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("缺少 Gemini API Key。请在 appsettings.Local.json 的 geminiApiKey 中配置，或设置 GEMINI_API_KEY 环境变量。");
        }

        try
        {
            var prompt = JapaneseAnalysisPrompt.Build(request);
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

            if (string.IsNullOrWhiteSpace(generated))
            {
                throw new InvalidOperationException("Gemini API 没有返回可解析的文本。");
            }

            return JsonSerializer.Deserialize<JapaneseAnalysisResult>(generated, JsonOptions)
                ?? throw new InvalidOperationException("Gemini 返回的 JSON 无法解析为分析结果。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Gemini 返回的 JSON 无法解析：{ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(GoogleApiErrorFormatter.FormatSdkException("Gemini API", ex), ex);
        }
    }
}

internal static class JapaneseAnalysisPrompt
{
    public static string Build(AnalyzeTextRequest request)
    {
        var language = request.LanguageMode switch
        {
            InputLanguageMode.Chinese => "中文",
            InputLanguageMode.Japanese => "日语",
            _ => "自动判断"
        };

        return $$"""
你是面向中文母语者的日语学习老师。请分析用户输入，并只返回合法 JSON，不要使用 Markdown。

任务：
1. 如果输入是中文，先翻译成日语，再给出自然日语和学习讲解。
2. 如果输入是日语，检查语法、助词、动词变形、歧义、自然度、简体/丁寧語/敬语。
3. 用中文解释每个问题，区分真正错误和自然度优化。
4. 给出适合 TTS 跟读的 readingOptimizedJapanese，长句要适当断句。

输入语言模式：{{language}}
使用场景：{{request.Scenario}}
目标文体：{{request.TargetStyle}}

JSON 结构必须是：
{
  "detectedLanguage": "Chinese 或 Japanese",
  "translatedJapanese": "",
  "correctedJapanese": "",
  "naturalJapanese": "",
  "plainFormJapanese": "",
  "politeFormJapanese": "",
  "businessKeigoJapanese": "",
  "readingOptimizedJapanese": "",
  "summaryZh": "",
  "issues": [
    {
      "type": "Particle|VerbConjugation|Tense|Politeness|Ambiguity|WordChoice|Collocation|ChineseLikeExpression|Naturalness|Punctuation|Other",
      "severity": "Error|Warning|Suggestion",
      "original": "",
      "suggestion": "",
      "explanationZh": "",
      "jlptLevel": "N5|N4|N3|N2|N1|Unknown",
      "exampleJapanese": "",
      "exampleChinese": "",
      "isHardError": true
    }
  ],
  "ambiguities": [
    {
      "sourceExpression": "",
      "riskZh": "",
      "recommendedJapanese": ""
    }
  ],
  "studyTips": [
    {
      "titleZh": "",
      "contentZh": "",
      "jlptLevel": "N5|N4|N3|N2|N1|Unknown"
    }
  ]
}

用户输入：
{{request.Text}}
""";
    }
}
