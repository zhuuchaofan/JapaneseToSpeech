namespace JapaneseLearningAssistant.Core.Models;

public sealed class JapaneseAnalysisResult
{
    public string DetectedLanguage { get; set; } = "";
    public string TranslatedJapanese { get; set; } = "";
    public string CorrectedJapanese { get; set; } = "";
    public string NaturalJapanese { get; set; } = "";
    public string PlainFormJapanese { get; set; } = "";
    public string PoliteFormJapanese { get; set; } = "";
    public string BusinessKeigoJapanese { get; set; } = "";
    public string ReadingOptimizedJapanese { get; set; } = "";
    public string SummaryZh { get; set; } = "";
    public List<JapaneseIssue> Issues { get; set; } = [];
    public List<AmbiguityNote> Ambiguities { get; set; } = [];
    public List<StudyTip> StudyTips { get; set; } = [];

    public string GetTextForStyle(JapaneseStyle style) => style switch
    {
        JapaneseStyle.Corrected => CorrectedJapanese,
        JapaneseStyle.Natural => NaturalJapanese,
        JapaneseStyle.Plain => PlainFormJapanese,
        JapaneseStyle.Polite => PoliteFormJapanese,
        JapaneseStyle.BusinessKeigo => BusinessKeigoJapanese,
        JapaneseStyle.ReadingOptimized => ReadingOptimizedJapanese,
        _ => NaturalJapanese
    };
}

public sealed class JapaneseIssue
{
    public string Type { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Original { get; set; } = "";
    public string Suggestion { get; set; } = "";
    public string ExplanationZh { get; set; } = "";
    public string JlptLevel { get; set; } = "";
    public string ExampleJapanese { get; set; } = "";
    public string ExampleChinese { get; set; } = "";
    public bool IsHardError { get; set; }
}

public sealed class AmbiguityNote
{
    public string SourceExpression { get; set; } = "";
    public string RiskZh { get; set; } = "";
    public string RecommendedJapanese { get; set; } = "";
}

public sealed class StudyTip
{
    public string TitleZh { get; set; } = "";
    public string ContentZh { get; set; } = "";
    public string JlptLevel { get; set; } = "";
}
