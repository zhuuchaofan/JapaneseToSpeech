using JapaneseLearningAssistant.Core.Models;

namespace JapaneseLearningAssistant.App.ViewModels;

public sealed class IssueViewModel
{
    public IssueViewModel(JapaneseIssue issue)
    {
        Type = issue.Type;
        TypeDisplay = LocalizeType(issue.Type);
        Severity = issue.Severity;
        SeverityDisplay = LocalizeSeverity(issue.Severity);
        Original = issue.Original;
        Suggestion = issue.Suggestion;
        ExplanationZh = issue.ExplanationZh;
        JlptLevel = issue.JlptLevel;
        ExampleJapanese = issue.ExampleJapanese;
        ExampleChinese = issue.ExampleChinese;
        IsHardError = issue.IsHardError;
    }

    public string Type { get; }
    public string TypeDisplay { get; }
    public string Severity { get; }
    public string SeverityDisplay { get; }
    public string Original { get; }
    public string Suggestion { get; }
    public string ExplanationZh { get; }
    public string JlptLevel { get; }
    public string ExampleJapanese { get; }
    public string ExampleChinese { get; }
    public bool IsHardError { get; }
    public bool HasJlptLevel => !string.IsNullOrWhiteSpace(JlptLevel);
    public bool HasExampleJapanese => !string.IsNullOrWhiteSpace(ExampleJapanese);
    public bool HasExampleChinese => !string.IsNullOrWhiteSpace(ExampleChinese);
    public string ErrorKindText => IsHardError ? "硬错误" : "自然度优化";

    private static string LocalizeType(string type) => type switch
    {
        "Particle" => "助词",
        "VerbConjugation" => "动词变形",
        "Tense" => "时态",
        "Politeness" => "礼貌表达",
        "Ambiguity" => "歧义",
        "WordChoice" => "用词选择",
        "Collocation" => "搭配",
        "ChineseLikeExpression" => "中文式表达",
        "Naturalness" => "自然度",
        "Punctuation" => "标点",
        "Other" => "其他",
        _ => string.IsNullOrWhiteSpace(type) ? "问题" : type
    };

    private static string LocalizeSeverity(string severity) => severity.Trim().ToLowerInvariant() switch
    {
        "error" or "high" or "serious" => "严重",
        "warning" or "medium" => "建议修改",
        "info" or "low" => "可优化",
        _ => string.IsNullOrWhiteSpace(severity) ? "未分级" : severity
    };
}
