namespace JapaneseLearningAssistant.Core.Models;

public sealed class SentenceAudioItem
{
    public int Index { get; init; }
    public string Text { get; init; } = "";
    public string AudioFilePath { get; init; } = "";
    public TimeSpan? Duration { get; init; }
}
