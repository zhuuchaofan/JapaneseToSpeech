using JapaneseLearningAssistant.Core.Models;
using JapaneseLearningAssistant.Core.Services;
using CommunityToolkit.Mvvm.Input;

namespace JapaneseLearningAssistant.App.ViewModels;

public sealed class HistoryItemViewModel
{
    public HistoryItemViewModel(HistoryEntry entry, Action<HistoryItemViewModel>? restore = null)
    {
        CreatedAt = entry.CreatedAt;
        OriginalText = entry.OriginalText;
        AnalysisResult = entry.AnalysisResult;
        TimeText = entry.CreatedAt.ToString("yyyy-MM-dd HH:mm");
        Title = Trim(entry.OriginalText, 42);
        Summary = Trim(entry.AnalysisResult.SummaryZh, 80);
        IssueCountText = entry.AnalysisResult.Issues.Count > 0
            ? $"{entry.AnalysisResult.Issues.Count} 个问题"
            : "无明显问题";
        RestoreCommand = new RelayCommand(() => restore?.Invoke(this), () => restore is not null);
    }

    public DateTimeOffset CreatedAt { get; }
    public string OriginalText { get; }
    public JapaneseAnalysisResult AnalysisResult { get; }
    public string TimeText { get; }
    public string Title { get; }
    public string Summary { get; }
    public string IssueCountText { get; }
    public IRelayCommand RestoreCommand { get; }

    private static string Trim(string text, int maxLength)
    {
        text = text.ReplaceLineEndings(" ");
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
