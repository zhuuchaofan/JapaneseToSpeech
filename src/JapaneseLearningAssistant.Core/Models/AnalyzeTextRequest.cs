namespace JapaneseLearningAssistant.Core.Models;

public sealed class AnalyzeTextRequest
{
    public string Text { get; init; } = "";
    public InputLanguageMode LanguageMode { get; init; } = InputLanguageMode.Auto;
    public string Scenario { get; init; } = "日语学习";
    public string TargetStyle { get; init; } = "自然、适合学习者理解";
}
