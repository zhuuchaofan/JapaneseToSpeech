using System.Text.Json;
using JapaneseLearningAssistant.Core.Models;

namespace JapaneseLearningAssistant.Core.Services;

public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _historyFilePath;

    public HistoryStore(string appDataDirectory)
    {
        Directory.CreateDirectory(appDataDirectory);
        _historyFilePath = Path.Combine(appDataDirectory, "history.json");
    }

    public async Task SaveAsync(HistoryEntry entry, CancellationToken cancellationToken)
    {
        var entries = await LoadAsync(cancellationToken);
        entries.Insert(0, entry);
        if (entries.Count > 100)
        {
            entries = entries.Take(100).ToList();
        }

        await File.WriteAllTextAsync(_historyFilePath, JsonSerializer.Serialize(entries, JsonOptions), cancellationToken);
    }

    public async Task<List<HistoryEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_historyFilePath))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(_historyFilePath, cancellationToken);
        return JsonSerializer.Deserialize<List<HistoryEntry>>(json, JsonOptions) ?? [];
    }
}

public sealed class HistoryEntry
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string OriginalText { get; set; } = "";
    public string AnalysisProvider { get; set; } = "";
    public string AnalysisModel { get; set; } = "";
    public JapaneseAnalysisResult AnalysisResult { get; set; } = new();
    public string? AudioFilePath { get; set; }
}
