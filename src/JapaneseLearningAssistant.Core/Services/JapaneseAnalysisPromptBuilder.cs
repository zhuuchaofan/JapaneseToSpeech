using JapaneseLearningAssistant.Core.Models;

namespace JapaneseLearningAssistant.Core.Services;

internal static class JapaneseAnalysisPromptBuilder
{
    public static string Build(AnalyzeTextRequest request)
    {
        var language = request.LanguageMode switch
        {
            InputLanguageMode.Chinese => "中文",
            InputLanguageMode.Japanese => "日语",
            _ => "自动判断"
        };

        var template = string.IsNullOrWhiteSpace(request.PromptTemplate) 
            ? PromptProfileManager.GetDefaultProfile().Template 
            : request.PromptTemplate;

        return template
            .Replace("{{language}}", language)
            .Replace("{{request.Scenario}}", BuildExpressionStyleInstruction(request.Scenario))
            .Replace("{{request.TargetStyle}}", request.TargetStyle)
            .Replace("{{request.Text}}", request.Text);
    }

    private static string BuildExpressionStyleInstruction(string value)
    {
        return value switch
        {
            "简体" => "表达风格：简体。请优先使用自然的普通体/简体表达，适合朋友、日记、轻松口语，不要过度敬语化。",
            "敬语" => "表达风格：敬语。请优先使用自然的です・ます体，礼貌但不过度商务化，适合日常学习和一般交流。",
            "商务" => "表达风格：商务。请优先使用得体、正式、自然的商务日语，必要时使用尊敬语和谦让语。",
            _ => value
        };
    }
}
