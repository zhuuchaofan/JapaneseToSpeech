using JapaneseLearningAssistant.Core.Models;

namespace JapaneseLearningAssistant.Core.Services;

public interface ITextToSpeechService
{
    Task<AudioGenerationResult> GenerateAsync(TtsRequest request, CancellationToken cancellationToken);
}
