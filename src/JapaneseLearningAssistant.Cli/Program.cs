using JapaneseLearningAssistant.Core.Configuration;
using JapaneseLearningAssistant.Core.Models;
using JapaneseLearningAssistant.Core.Services;

Console.WriteLine("日语学习助手 CLI MVP");
Console.WriteLine("输入中文或日语，按空行开始分析。");
AppLogger.Info("CLI started.");

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
    AppLogger.Warning("CLI exited because input text was empty.");
    Console.WriteLine("没有输入文本。");
    return;
}

var config = LocalAppConfig.Load();
var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JapaneseLearningAssistant");

var analysisClient = AnalysisClientFactory.Create(config);
var googleTts = new GoogleTextToSpeechService(
    new HttpClient { Timeout = TimeSpan.FromSeconds(120) },
    config.GoogleTtsApiKey,
    Path.Combine(appData, "audio-temp"));
var tts = new TtsAudioCacheService(
    googleTts,
    Path.Combine(appData, "audio-cache"),
    "GoogleCloudTextToSpeech");
var history = new HistoryStore(appData);

try
{
    AppLogger.Info($"CLI analysis started. TextLength={text.Length}.");
    var result = await analysisClient.AnalyzeAsync(new AnalyzeTextRequest
    {
        Text = text,
        LanguageMode = InputLanguageMode.Auto,
        Scenario = "日常学习",
        TargetStyle = "自然版"
    }, CancellationToken.None);
    AppLogger.Info($"CLI analysis completed. Issues={result.Issues.Count}.");

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
        AnalysisProvider = config.AnalysisProvider,
        AnalysisModel = config.GetAnalysisModel(),
        AnalysisResult = result
    }, CancellationToken.None);

    Console.WriteLine();
    Console.Write("是否生成日语语音？y/N: ");
    if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true)
    {
        AppLogger.Info("CLI TTS generation requested.");
        var audio = await tts.GenerateAsync(new TtsRequest { Text = result.ReadingOptimizedJapanese }, CancellationToken.None);
        Console.WriteLine($"音频文件：{audio.FilePath}");
        AppLogger.Info($"CLI TTS generation completed. FilePath={audio.FilePath}.");
    }
}
catch (Exception ex)
{
    AppLogger.Error(ex, "CLI operation failed.");
    Console.WriteLine(ex.Message);
}
