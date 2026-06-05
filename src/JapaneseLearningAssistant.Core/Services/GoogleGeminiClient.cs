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

核心目标：
- 把中文或不自然的日语转成「地道、自然、符合日本人实际表达习惯」的日语。
- 不要逐字硬翻中文，不要保留中文式表达；优先使用日本人日常会说/会写的表达。
- 根据使用场景调整语气：日常学习偏自然易懂，作文修改偏书面清楚，口语表达偏自然口语，邮件/商务偏礼貌正式，自我介绍偏得体自然，JLPT 练习偏学习说明清楚。
- 默认结果不要过度敬语化；只有场景需要时才提高礼貌度。

任务：
1. 如果输入是中文，翻译成自然地道的日语。
2. 如果输入是日语，修正错误并提升自然度。
3. 输出三种主要版本：
   - correctedJapanese：修正版，尽量保留原句结构，只修明显错误和不自然处。
   - naturalJapanese：自然版，作为默认最终译文，要最地道、最符合场景。
   - readingOptimizedJapanese：朗读版，基于自然版，拆成长短适合 TTS 和跟读的句子。
4. 用中文解释最重要的问题，优先列 3 到 6 条；没有明显问题时 issues 可以为空数组。
5. plainFormJapanese、politeFormJapanese、businessKeigoJapanese 是兼容旧版本字段，本次请返回空字符串，除非这些内容和自然版完全必要。

输入语言模式：{{language}}
使用场景：{{request.Scenario}}
当前查看版本：{{request.TargetStyle}}

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
