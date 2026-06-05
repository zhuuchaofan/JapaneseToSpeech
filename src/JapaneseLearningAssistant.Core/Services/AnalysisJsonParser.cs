using System.Text.Json;
using JapaneseLearningAssistant.Core.Models;

namespace JapaneseLearningAssistant.Core.Services;

internal static class AnalysisJsonParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static JapaneseAnalysisResult Parse(string content, string providerName)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException($"{providerName} 没有返回可解析的文本。");
        }

        var json = ExtractJson(content);
        try
        {
            return JsonSerializer.Deserialize<JapaneseAnalysisResult>(json, JsonOptions)
                ?? throw new InvalidOperationException($"{providerName} 返回的 JSON 无法解析为分析结果。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{providerName} 返回的 JSON 无法解析：{ex.Message}", ex);
        }
    }

    private static string ExtractJson(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed.Trim('`').Trim();
            if (trimmed.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[4..].Trim();
            }
        }

        var first = trimmed.IndexOf('{');
        var last = trimmed.LastIndexOf('}');
        if (first >= 0 && last > first)
        {
            return trimmed[first..(last + 1)];
        }

        return trimmed;
    }
}
