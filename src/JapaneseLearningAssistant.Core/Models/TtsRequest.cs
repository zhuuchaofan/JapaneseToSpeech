namespace JapaneseLearningAssistant.Core.Models;

public sealed class TtsRequest
{
    public string Text { get; init; } = "";
    public string LanguageCode { get; init; } = "ja-JP";
    public string VoiceName { get; init; } = "ja-JP-Neural2-B";
    public double SpeakingRate { get; init; } = 0.85;
    public double Pitch { get; init; }
    public string AudioFormat { get; init; } = "MP3";
}

public sealed class AudioGenerationResult
{
    public string FilePath { get; init; } = "";
    public string Provider { get; init; } = "";
    public string VoiceName { get; init; } = "";
}
