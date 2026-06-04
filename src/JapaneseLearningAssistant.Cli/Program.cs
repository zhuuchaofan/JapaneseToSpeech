using JapaneseLearningAssistant.Core.Configuration;
using JapaneseLearningAssistant.Core.Models;
using JapaneseLearningAssistant.Core.Services;

Console.WriteLine("日语学习助手 CLI MVP");
Console.WriteLine("输入中文或日语，按空行开始分析。");

var lines = new List<string>();
while (true)
{
    var line = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(line))
    {
        break;
    }

    lines.Add(line);
}

var text = string.Join(Environment.NewLine, lines);
if (string.IsNullOrWhiteSpace(text))
{
    Console.WriteLine("没有输入文本。");
    return;
}

var config = LocalAppConfig.Load();
var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JapaneseLearningAssistant");

var gemini = new GoogleGeminiClient(new HttpClient { Timeout = TimeSpan.FromSeconds(120) }, config.GeminiApiKey, config.GeminiModel);
var tts = new GoogleTextToSpeechService(new HttpClient { Timeout = TimeSpan.FromSeconds(120) }, config.GoogleTtsApiKey, Path.Combine(appData, "audio"));
var history = new HistoryStore(appData);

try
{
    var result = await gemini.AnalyzeAsync(new AnalyzeTextRequest
    {
        Text = text,
        LanguageMode = InputLanguageMode.Auto,
        Scenario = "日语学习",
        TargetStyle = "自然版"
    }, CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine("概要：");
    Console.WriteLine(result.SummaryZh);
    Console.WriteLine();
    Console.WriteLine("自然版：");
    Console.WriteLine(result.NaturalJapanese);
    Console.WriteLine();
    Console.WriteLine("问题：");
    foreach (var issue in result.Issues)
    {
        Console.WriteLine($"- [{issue.Type}] {issue.Original} -> {issue.Suggestion}");
        Console.WriteLine($"  {issue.ExplanationZh}");
    }

    await history.SaveAsync(new HistoryEntry
    {
        OriginalText = text,
        AnalysisResult = result
    }, CancellationToken.None);

    Console.WriteLine();
    Console.Write("是否生成日语语音？y/N: ");
    if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true)
    {
        var audio = await tts.GenerateAsync(new TtsRequest { Text = result.ReadingOptimizedJapanese }, CancellationToken.None);
        Console.WriteLine($"音频文件：{audio.FilePath}");
    }
}
catch (Exception ex)
{
    Console.WriteLine(ex.Message);
}
