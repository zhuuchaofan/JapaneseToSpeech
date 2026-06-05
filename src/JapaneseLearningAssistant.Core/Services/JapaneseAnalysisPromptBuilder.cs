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
            .Replace("{{request.Scenario}}", request.Scenario)
            .Replace("{{request.TargetStyle}}", request.TargetStyle)
            .Replace("{{request.Text}}", request.Text);
    }
}
