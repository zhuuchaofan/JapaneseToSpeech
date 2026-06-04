using JapaneseLearningAssistant.Core.Models;

namespace JapaneseLearningAssistant.Core.Services;

public interface IGeminiClient
{
    Task<JapaneseAnalysisResult> AnalyzeAsync(AnalyzeTextRequest request, CancellationToken cancellationToken);
}
