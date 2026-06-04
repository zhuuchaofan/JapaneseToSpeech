using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JapaneseLearningAssistant.App.Services;
using JapaneseLearningAssistant.Core.Configuration;
using JapaneseLearningAssistant.Core.Models;
using JapaneseLearningAssistant.Core.Services;

namespace JapaneseLearningAssistant.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IGeminiClient _geminiClient;
    private readonly ITextToSpeechService _textToSpeechService;
    private readonly HistoryStore _historyStore;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly DispatcherTimer _playbackTimer;
    private JapaneseAnalysisResult? _latestResult;
    private string? _latestAudioFilePath;
    private string? _latestAudioSourceText;
    private string? _latestAudioVoiceName;
    private double? _latestAudioSpeakingRate;
    private bool _isUpdatingPlaybackPosition;

    public MainWindowViewModel()
        : this(CreateGeminiClient(), CreateTtsService(), CreateHistoryStore(), new NAudioPlaybackService())
    {
    }

    public MainWindowViewModel(
        IGeminiClient geminiClient,
        ITextToSpeechService textToSpeechService,
        HistoryStore historyStore,
        IAudioPlaybackService audioPlaybackService)
    {
        _geminiClient = geminiClient;
        _textToSpeechService = textToSpeechService;
        _historyStore = historyStore;
        _audioPlaybackService = audioPlaybackService;
        _audioPlaybackService.PlaybackEnded += OnPlaybackEnded;
        _audioPlaybackService.StateChanged += OnPlaybackStateChanged;
        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _playbackTimer.Tick += (_, _) => UpdatePlaybackProgress();
        _playbackTimer.Start();
        _ = LoadHistoryAsync();
    }

    public string[] LanguageModes { get; } = ["自动判断", "中文", "日语"];
    public string[] Scenarios { get; } = ["日语学习", "作文练习", "口语稿", "邮件", "自我介绍", "JLPT 练习"];
    public string[] Styles { get; } = ["修正版", "自然版", "普通体", "丁寧語", "商务敬语", "朗读优化"];
    public string[] Voices { get; } = ["ja-JP-Neural2-B", "ja-JP-Neural2-C", "ja-JP-Wavenet-B", "ja-JP-Wavenet-C"];

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private string _selectedLanguageMode = "自动判断";

    [ObservableProperty]
    private string _selectedScenario = "日语学习";

    [ObservableProperty]
    private string _selectedStyle = "自然版";

    [ObservableProperty]
    private string _selectedVoice = LocalAppConfig.Load().GoogleTtsVoiceName;

    [ObservableProperty]
    private double _speakingRate = 0.85;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "准备就绪。请配置 appsettings.Local.json 或环境变量后开始。";

    [ObservableProperty]
    private string _summaryText = "";

    [ObservableProperty]
    private string _selectedOutputText = "";

    [ObservableProperty]
    private bool _isAudioPlaying;

    [ObservableProperty]
    private bool _isAudioPaused;

    [ObservableProperty]
    private bool _hasLoadedAudio;

    [ObservableProperty]
    private bool _isLoopAudio;

    [ObservableProperty]
    private double _playbackPositionSeconds;

    [ObservableProperty]
    private double _playbackDurationSeconds;

    [ObservableProperty]
    private string _playbackTimeText = "00:00 / 00:00";

    public string PlayAudioButtonText => IsAudioPlaying ? "暂停" : IsAudioPaused ? "继续" : "播放";

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
    private async Task PlayAudioAsync()
    {
        var text = GetTextForSelectedStyle();
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText = "请先分析文本，或选择一个有内容的输出版本。";
            return;
        }

        if (HasPlayableAudioFor(text))
        {
            EnsureLatestAudioLoaded();
            TogglePlayback();
            return;
        }

        await RunBusyAsync("语音生成中...", async cancellationToken =>
        {
            await GenerateSpeechForCurrentSelectionAsync(text, cancellationToken);
            EnsureLatestAudioLoaded();
            _audioPlaybackService.Play();
            UpdateAudioStateProperties();
            StatusText = "正在播放。";
        });
    }

    [RelayCommand]
    private void StopAudio()
    {
        _audioPlaybackService.Stop();
        UpdatePlaybackProgress();
        UpdateAudioStateProperties();
        StatusText = "播放已停止。";
    }

    partial void OnSelectedStyleChanged(string value)
    {
        StopCurrentPlayback();
        SelectedOutputText = GetTextForSelectedStyle();
    }

    partial void OnSelectedVoiceChanged(string value)
    {
        InvalidateCurrentAudio();
    }

    partial void OnSpeakingRateChanged(double value)
    {
        InvalidateCurrentAudio();
    }

    partial void OnIsAudioPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayAudioButtonText));
    }

    partial void OnIsAudioPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayAudioButtonText));
    }

    partial void OnPlaybackPositionSecondsChanged(double value)
    {
        if (_isUpdatingPlaybackPosition || PlaybackDurationSeconds <= 0)
        {
            return;
        }

        _audioPlaybackService.Seek(TimeSpan.FromSeconds(value));
        UpdatePlaybackProgress();
    }

    private void ApplyAnalysisResult(JapaneseAnalysisResult result)
    {
        _latestResult = result;
        InvalidateCurrentAudio();

        SummaryText = result.SummaryZh;
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

    private async Task<AudioGenerationResult> GenerateSpeechForCurrentSelectionAsync(string text, CancellationToken cancellationToken)
    {
        var audio = await _textToSpeechService.GenerateAsync(new TtsRequest
        {
            Text = text,
            VoiceName = SelectedVoice,
            SpeakingRate = SpeakingRate
        }, cancellationToken);

        _latestAudioFilePath = audio.FilePath;
        _latestAudioSourceText = text;
        _latestAudioVoiceName = SelectedVoice;
        _latestAudioSpeakingRate = SpeakingRate;

        return audio;
    }

    private bool HasPlayableAudioFor(string text)
    {
        return !string.IsNullOrWhiteSpace(_latestAudioFilePath)
            && File.Exists(_latestAudioFilePath)
            && string.Equals(_latestAudioSourceText, text, StringComparison.Ordinal)
            && string.Equals(_latestAudioVoiceName, SelectedVoice, StringComparison.Ordinal)
            && _latestAudioSpeakingRate.HasValue
            && Math.Abs(_latestAudioSpeakingRate.Value - SpeakingRate) < 0.001;
    }

    private void EnsureLatestAudioLoaded()
    {
        if (string.IsNullOrWhiteSpace(_latestAudioFilePath))
        {
            throw new FileNotFoundException("音频文件不存在。", _latestAudioFilePath);
        }

        if (!string.Equals(_audioPlaybackService.LoadedFilePath, _latestAudioFilePath, StringComparison.Ordinal))
        {
            _audioPlaybackService.Load(_latestAudioFilePath);
            UpdatePlaybackProgress();
            UpdateAudioStateProperties();
        }
    }

    private void TogglePlayback()
    {
        if (_audioPlaybackService.State == AudioPlaybackState.Playing)
        {
            _audioPlaybackService.Pause();
            StatusText = "播放已暂停。";
        }
        else
        {
            _audioPlaybackService.Play();
            StatusText = "正在播放。";
        }

        UpdateAudioStateProperties();
    }

    private void InvalidateCurrentAudio()
    {
        StopCurrentPlayback();
        _latestAudioFilePath = null;
        _latestAudioSourceText = null;
        _latestAudioVoiceName = null;
        _latestAudioSpeakingRate = null;
    }

    private void StopCurrentPlayback()
    {
        if (_audioPlaybackService.State is AudioPlaybackState.Playing or AudioPlaybackState.Paused)
        {
            _audioPlaybackService.Stop();
        }

        UpdatePlaybackProgress();
        UpdateAudioStateProperties();
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsLoopAudio)
            {
                _audioPlaybackService.Play();
                StatusText = "正在循环播放。";
            }
            else
            {
                StatusText = "播放完成。";
            }

            UpdatePlaybackProgress();
            UpdateAudioStateProperties();
        });
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateAudioStateProperties);
    }

    private void UpdateAudioStateProperties()
    {
        IsAudioPlaying = _audioPlaybackService.State == AudioPlaybackState.Playing;
        IsAudioPaused = _audioPlaybackService.State == AudioPlaybackState.Paused;
        HasLoadedAudio = _audioPlaybackService.State != AudioPlaybackState.Empty;
        OnPropertyChanged(nameof(PlayAudioButtonText));
    }

    private void UpdatePlaybackProgress()
    {
        var duration = _audioPlaybackService.Duration;
        var position = _audioPlaybackService.Position;

        _isUpdatingPlaybackPosition = true;
        PlaybackDurationSeconds = duration.TotalSeconds;
        PlaybackPositionSeconds = Math.Min(position.TotalSeconds, Math.Max(duration.TotalSeconds, 0));
        PlaybackTimeText = $"{FormatDuration(position)} / {FormatDuration(duration)}";
        _isUpdatingPlaybackPosition = false;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"mm\:ss");
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

    private static GoogleGeminiClient CreateGeminiClient()
    {
        var config = LocalAppConfig.Load();
        return new GoogleGeminiClient(new HttpClient { Timeout = TimeSpan.FromSeconds(120) }, config.GeminiApiKey, config.GeminiModel);
    }

    private static GoogleTextToSpeechService CreateTtsService()
    {
        var config = LocalAppConfig.Load();
        return new GoogleTextToSpeechService(
            new HttpClient { Timeout = TimeSpan.FromSeconds(120) },
            config.GoogleTtsApiKey,
            Path.Combine(GetAppDataDirectory(), "audio"));
    }

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
