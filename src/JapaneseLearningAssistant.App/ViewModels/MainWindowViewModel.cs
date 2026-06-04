using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JapaneseLearningAssistant.Core.Models;
using JapaneseLearningAssistant.Core.Services;

namespace JapaneseLearningAssistant.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IGeminiClient _geminiClient;
    private readonly ITextToSpeechService _textToSpeechService;
    private readonly HistoryStore _historyStore;
    private JapaneseAnalysisResult? _latestResult;
    private string? _latestAudioFilePath;

    public MainWindowViewModel()
        : this(CreateGeminiClient(), CreateTtsService(), CreateHistoryStore())
    {
    }

    public MainWindowViewModel(
        IGeminiClient geminiClient,
        ITextToSpeechService textToSpeechService,
        HistoryStore historyStore)
    {
        _geminiClient = geminiClient;
        _textToSpeechService = textToSpeechService;
        _historyStore = historyStore;
        _ = LoadHistoryAsync();
    }

    public string[] LanguageModes { get; } = ["自动判断", "中文", "日语"];
    public string[] Scenarios { get; } = ["日语学习", "作文练习", "口语稿", "邮件", "自我介绍", "JLPT 练习"];
    public string[] Styles { get; } = ["修正版", "自然版", "普通体", "丁寧語", "商务敬语", "朗读优化"];
    public string[] Voices { get; } = ["ja-JP-Neural2-B", "ja-JP-Neural2-C", "ja-JP-Wavenet-B", "ja-JP-Wavenet-C"];

    [ObservableProperty]
    private string _inputText = "我明天把资料发给你，请确认一下。";

    [ObservableProperty]
    private string _selectedLanguageMode = "自动判断";

    [ObservableProperty]
    private string _selectedScenario = "日语学习";

    [ObservableProperty]
    private string _selectedStyle = "自然版";

    [ObservableProperty]
    private string _selectedVoice = "ja-JP-Neural2-B";

    [ObservableProperty]
    private double _speakingRate = 0.85;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "准备就绪。请配置 GEMINI_API_KEY 和 GOOGLE_TTS_API_KEY 后开始。";

    [ObservableProperty]
    private string _summaryText = "";

    [ObservableProperty]
    private string _translatedJapanese = "";

    [ObservableProperty]
    private string _correctedJapanese = "";

    [ObservableProperty]
    private string _naturalJapanese = "";

    [ObservableProperty]
    private string _plainJapanese = "";

    [ObservableProperty]
    private string _politeJapanese = "";

    [ObservableProperty]
    private string _businessKeigoJapanese = "";

    [ObservableProperty]
    private string _readingOptimizedJapanese = "";

    [ObservableProperty]
    private string _selectedOutputText = "";

    [ObservableProperty]
    private string _audioFilePath = "";

    public ObservableCollection<IssueViewModel> Issues { get; } = [];
    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; } = [];

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText))
        {
            StatusText = "请输入中文或日语文本。";
            return;
        }

        await RunBusyAsync("正在调用 Gemini 分析文本...", async cancellationToken =>
        {
            var request = new AnalyzeTextRequest
            {
                Text = InputText.Trim(),
                LanguageMode = ParseLanguageMode(SelectedLanguageMode),
                Scenario = SelectedScenario,
                TargetStyle = SelectedStyle
            };

            var result = await _geminiClient.AnalyzeAsync(request, cancellationToken);
            ApplyAnalysisResult(result);

            await _historyStore.SaveAsync(new HistoryEntry
            {
                CreatedAt = DateTimeOffset.Now,
                OriginalText = InputText,
                AnalysisResult = result
            }, cancellationToken);

            await LoadHistoryAsync();
            StatusText = "分析完成。";
        });
    }

    [RelayCommand]
    private async Task GenerateSpeechAsync()
    {
        var text = GetTextForSelectedStyle();
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText = "请先分析文本，或选择一个有内容的输出版本。";
            return;
        }

        await RunBusyAsync("正在生成日语语音...", async cancellationToken =>
        {
            var audio = await _textToSpeechService.GenerateAsync(new TtsRequest
            {
                Text = text,
                VoiceName = SelectedVoice,
                SpeakingRate = SpeakingRate
            }, cancellationToken);

            _latestAudioFilePath = audio.FilePath;
            AudioFilePath = audio.FilePath;
            StatusText = $"语音已生成：{audio.FilePath}";
        });
    }

    [RelayCommand]
    private void PlayAudio()
    {
        try
        {
            LocalAudioPlayer.Play(AudioFilePath);
            StatusText = "正在播放音频。";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    [RelayCommand]
    private void RefreshSelectedOutput()
    {
        SelectedOutputText = GetTextForSelectedStyle();
    }

    partial void OnSelectedStyleChanged(string value)
    {
        SelectedOutputText = GetTextForSelectedStyle();
    }

    private void ApplyAnalysisResult(JapaneseAnalysisResult result)
    {
        _latestResult = result;
        _latestAudioFilePath = null;
        AudioFilePath = "";

        SummaryText = result.SummaryZh;
        TranslatedJapanese = result.TranslatedJapanese;
        CorrectedJapanese = result.CorrectedJapanese;
        NaturalJapanese = result.NaturalJapanese;
        PlainJapanese = result.PlainFormJapanese;
        PoliteJapanese = result.PoliteFormJapanese;
        BusinessKeigoJapanese = result.BusinessKeigoJapanese;
        ReadingOptimizedJapanese = result.ReadingOptimizedJapanese;
        SelectedOutputText = GetTextForSelectedStyle();

        Issues.Clear();
        foreach (var issue in result.Issues)
        {
            Issues.Add(new IssueViewModel(issue));
        }
    }

    private string GetTextForSelectedStyle()
    {
        if (_latestResult is null)
        {
            return "";
        }

        var style = SelectedStyle switch
        {
            "修正版" => JapaneseStyle.Corrected,
            "自然版" => JapaneseStyle.Natural,
            "普通体" => JapaneseStyle.Plain,
            "丁寧語" => JapaneseStyle.Polite,
            "商务敬语" => JapaneseStyle.BusinessKeigo,
            "朗读优化" => JapaneseStyle.ReadingOptimized,
            _ => JapaneseStyle.Natural
        };

        return _latestResult.GetTextForStyle(style);
    }

    private async Task RunBusyAsync(string busyText, Func<CancellationToken, Task> operation)
    {
        IsBusy = true;
        StatusText = busyText;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await operation(timeout.Token);
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var entries = await _historyStore.LoadAsync(CancellationToken.None);
            HistoryItems.Clear();
            foreach (var entry in entries.Take(20))
            {
                HistoryItems.Add(new HistoryItemViewModel(entry));
            }
        }
        catch
        {
            // History is optional for the MVP; UI should still open if the file is unreadable.
        }
    }

    private static InputLanguageMode ParseLanguageMode(string value) => value switch
    {
        "中文" => InputLanguageMode.Chinese,
        "日语" => InputLanguageMode.Japanese,
        _ => InputLanguageMode.Auto
    };

    private static GoogleGeminiClient CreateGeminiClient() =>
        new(new HttpClient { Timeout = TimeSpan.FromSeconds(120) }, Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "");

    private static GoogleTextToSpeechService CreateTtsService() =>
        new(
            new HttpClient { Timeout = TimeSpan.FromSeconds(120) },
            Environment.GetEnvironmentVariable("GOOGLE_TTS_API_KEY") ?? "",
            Path.Combine(GetAppDataDirectory(), "audio"));

    private static HistoryStore CreateHistoryStore() => new(GetAppDataDirectory());

    private static string GetAppDataDirectory()
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(baseDirectory, "JapaneseLearningAssistant");
    }
}

public sealed class IssueViewModel
{
    public IssueViewModel(JapaneseIssue issue)
    {
        Type = issue.Type;
        Severity = issue.Severity;
        Original = issue.Original;
        Suggestion = issue.Suggestion;
        ExplanationZh = issue.ExplanationZh;
        JlptLevel = issue.JlptLevel;
        ExampleJapanese = issue.ExampleJapanese;
        ExampleChinese = issue.ExampleChinese;
    }

    public string Type { get; }
    public string Severity { get; }
    public string Original { get; }
    public string Suggestion { get; }
    public string ExplanationZh { get; }
    public string JlptLevel { get; }
    public string ExampleJapanese { get; }
    public string ExampleChinese { get; }
}

public sealed class HistoryItemViewModel
{
    public HistoryItemViewModel(HistoryEntry entry)
    {
        Title = $"{entry.CreatedAt:yyyy-MM-dd HH:mm}  {Trim(entry.OriginalText)}";
        Summary = entry.AnalysisResult.SummaryZh;
    }

    public string Title { get; }
    public string Summary { get; }

    private static string Trim(string text)
    {
        text = text.ReplaceLineEndings(" ");
        return text.Length <= 36 ? text : text[..36] + "...";
    }
}
