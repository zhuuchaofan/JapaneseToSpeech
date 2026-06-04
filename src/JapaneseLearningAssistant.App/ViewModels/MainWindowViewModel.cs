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
    private bool _isSentencePlaybackActive;

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
        AppLogger.Info("MainWindowViewModel initialized.");
        _ = LoadHistoryAsync();
    }

    public string[] LanguageModes { get; } = ["自动判断", "中文", "日语"];
    public string[] Scenarios { get; } = ["日语学习", "作文练习", "口语稿", "邮件", "自我介绍", "JLPT 练习"];
    public string[] Styles { get; } = ["修正版", "自然版", "普通体", "丁寧語", "商务敬语", "朗读优化"];
    public string[] Voices { get; } = ["ja-JP-Neural2-B", "ja-JP-Neural2-C", "ja-JP-Wavenet-B", "ja-JP-Wavenet-C"];
    public string[] WholeAudioPlaybackModes { get; } = ["整段播放一次", "整段循环"];
    public string[] SentencePlaybackModes { get; } = ["手动逐句", "自动下一句", "单句循环"];

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
    private double _speakingRate = 1;

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
    private string _selectedWholeAudioPlaybackMode = "整段播放一次";

    [ObservableProperty]
    private double _playbackPositionSeconds;

    [ObservableProperty]
    private double _playbackDurationSeconds;

    [ObservableProperty]
    private string _playbackTimeText = "00:00 / 00:00";

    public string PlayAudioButtonText => IsAudioPlaying ? "暂停" : IsAudioPaused ? "继续" : "播放";
    public string PlayAudioIcon => IsAudioPlaying ? "⏸" : "▶";
    public string SentencePlayButtonText => _isSentencePlaybackActive && IsAudioPlaying ? "暂停当前句" : _isSentencePlaybackActive && IsAudioPaused ? "继续当前句" : "播放当前句";
    public string SentencePlayIcon => _isSentencePlaybackActive && IsAudioPlaying ? "⏸" : "▶";
    public bool CanPlaySelectedSentence => !IsBusy && SentenceAudioItems.Count > 0;
    public bool CanPlayPreviousSentence => !IsBusy && (CurrentSentenceAudioItem ?? SelectedSentenceAudioItem)?.Index > 0;
    public bool CanPlayNextSentence => !IsBusy && (CurrentSentenceAudioItem ?? SelectedSentenceAudioItem)?.Index < SentenceAudioItems.Count - 1;

    public ObservableCollection<IssueViewModel> Issues { get; } = [];
    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; } = [];
    public ObservableCollection<SentenceAudioItemViewModel> SentenceAudioItems { get; } = [];

    [ObservableProperty]
    private SentenceAudioItemViewModel? _selectedSentenceAudioItem;

    [ObservableProperty]
    private SentenceAudioItemViewModel? _currentSentenceAudioItem;

    [ObservableProperty]
    private string _selectedSentencePlaybackMode = "自动下一句";

    [ObservableProperty]
    private bool _isCompactLayout;

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
            AppLogger.Info($"Analysis started. TextLength={InputText.Trim().Length}, LanguageMode={SelectedLanguageMode}, Scenario={SelectedScenario}, TargetStyle={SelectedStyle}.");
            var request = new AnalyzeTextRequest
            {
                Text = InputText.Trim(),
                LanguageMode = ParseLanguageMode(SelectedLanguageMode),
                Scenario = SelectedScenario,
                TargetStyle = SelectedStyle
            };

            var result = await _geminiClient.AnalyzeAsync(request, cancellationToken);
            ApplyAnalysisResult(result);
            AppLogger.Info($"Analysis completed. Issues={result.Issues.Count}, SelectedStyle={SelectedStyle}, SentenceCount={SentenceAudioItems.Count}.");

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
        _isSentencePlaybackActive = false;
        AppLogger.Info($"Whole audio play requested. SelectedStyle={SelectedStyle}, VoiceName={SelectedVoice}, SpeakingRate={SpeakingRate}, Mode={SelectedWholeAudioPlaybackMode}.");
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
        _isSentencePlaybackActive = false;
        AppLogger.Info("Stop audio requested.");
        _audioPlaybackService.Stop();
        UpdatePlaybackProgress();
        UpdateAudioStateProperties();
        StatusText = "播放已停止。";
    }

    [RelayCommand]
    private async Task PlaySelectedSentenceAsync()
    {
        var sentence = SelectedSentenceAudioItem ?? CurrentSentenceAudioItem ?? SentenceAudioItems.FirstOrDefault();
        if (sentence is null)
        {
            StatusText = "请先分析文本，生成可逐句播放的内容。";
            return;
        }

        AppLogger.Info($"Selected sentence play requested. Index={sentence.Index}, TextLength={sentence.Text.Length}, Mode={SelectedSentencePlaybackMode}.");
        await PlaySentenceAsync(sentence, CancellationToken.None);
    }

    [RelayCommand]
    private async Task PlayPreviousSentenceAsync()
    {
        var sentence = GetRelativeSentence(-1);
        if (sentence is null)
        {
            StatusText = "已经是第一句。";
            return;
        }

        await PlaySentenceAsync(sentence, CancellationToken.None);
    }

    [RelayCommand]
    private async Task PlayNextSentenceAsync()
    {
        var sentence = GetRelativeSentence(1);
        if (sentence is null)
        {
            StatusText = "已经是最后一句。";
            return;
        }

        await PlaySentenceAsync(sentence, CancellationToken.None);
    }

    partial void OnSelectedStyleChanged(string value)
    {
        _isSentencePlaybackActive = false;
        StopCurrentPlayback();
        SelectedOutputText = GetTextForSelectedStyle();
        RefreshSentenceItems();
    }

    partial void OnSelectedVoiceChanged(string value)
    {
        InvalidateCurrentAudio();
        InvalidateSentenceAudio();
    }

    partial void OnSpeakingRateChanged(double value)
    {
        InvalidateCurrentAudio();
        InvalidateSentenceAudio();
    }

    partial void OnIsAudioPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayAudioButtonText));
        OnPropertyChanged(nameof(PlayAudioIcon));
    }

    partial void OnIsAudioPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayAudioButtonText));
        OnPropertyChanged(nameof(PlayAudioIcon));
    }

    partial void OnIsBusyChanged(bool value)
    {
        NotifySentenceControlProperties();
    }

    partial void OnSelectedSentenceAudioItemChanged(SentenceAudioItemViewModel? value)
    {
        NotifySentenceControlProperties();
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
        RefreshSentenceItems();

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
        AppLogger.Info($"Whole audio prepared. FilePath={audio.FilePath}, VoiceName={audio.VoiceName}.");

        return audio;
    }

    private async Task GenerateSpeechForSentenceAsync(SentenceAudioItemViewModel sentence, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sentence.AudioFilePath) && File.Exists(sentence.AudioFilePath))
        {
            return;
        }

        var audio = await _textToSpeechService.GenerateAsync(new TtsRequest
        {
            Text = sentence.Text,
            VoiceName = SelectedVoice,
            SpeakingRate = SpeakingRate
        }, cancellationToken);

        sentence.AudioFilePath = audio.FilePath;
        sentence.IsAudioReady = true;
        AppLogger.Info($"Sentence audio prepared. Index={sentence.Index}, FilePath={audio.FilePath}, VoiceName={audio.VoiceName}.");
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

    private async Task PlaySentenceAsync(SentenceAudioItemViewModel sentence, CancellationToken cancellationToken)
    {
        if (CurrentSentenceAudioItem == sentence
            && !string.IsNullOrWhiteSpace(sentence.AudioFilePath)
            && string.Equals(_audioPlaybackService.LoadedFilePath, sentence.AudioFilePath, StringComparison.Ordinal)
            && _audioPlaybackService.State is AudioPlaybackState.Playing or AudioPlaybackState.Paused)
        {
            _isSentencePlaybackActive = true;
            TogglePlayback();
            return;
        }

        await RunBusyAsync($"正在准备第 {sentence.Index + 1} 句语音...", async token =>
        {
            await GenerateSpeechForSentenceAsync(sentence, token);
            if (string.IsNullOrWhiteSpace(sentence.AudioFilePath))
            {
                throw new FileNotFoundException("句子音频文件不存在。", sentence.AudioFilePath);
            }

            SetCurrentSentence(sentence);
            _isSentencePlaybackActive = true;
            _audioPlaybackService.Load(sentence.AudioFilePath);
            _audioPlaybackService.Play();
            UpdatePlaybackProgress();
            UpdateAudioStateProperties();
            StatusText = $"正在播放第 {sentence.Index + 1} 句。";
            AppLogger.Info($"Sentence playback started. Index={sentence.Index}, Mode={SelectedSentencePlaybackMode}.");
        });
    }

    private async Task PlayNextSentenceFromPlaybackEndAsync()
    {
        var next = GetRelativeSentence(1);
        if (next is null)
        {
            _isSentencePlaybackActive = false;
            StatusText = "逐句播放完成。";
            AppLogger.Info("Sentence playback completed at final sentence.");
            UpdatePlaybackProgress();
            UpdateAudioStateProperties();
            return;
        }

        await PlaySentenceAsync(next, CancellationToken.None);
    }

    private SentenceAudioItemViewModel? GetRelativeSentence(int offset)
    {
        var current = CurrentSentenceAudioItem ?? SelectedSentenceAudioItem ?? SentenceAudioItems.FirstOrDefault();
        if (current is null)
        {
            return null;
        }

        var nextIndex = current.Index + offset;
        return SentenceAudioItems.FirstOrDefault(item => item.Index == nextIndex);
    }

    private void SetCurrentSentence(SentenceAudioItemViewModel? sentence)
    {
        if (CurrentSentenceAudioItem is not null)
        {
            CurrentSentenceAudioItem.IsCurrent = false;
        }

        CurrentSentenceAudioItem = sentence;
        SelectedSentenceAudioItem = sentence;

        if (CurrentSentenceAudioItem is not null)
        {
            CurrentSentenceAudioItem.IsCurrent = true;
        }

        NotifySentenceControlProperties();
    }

    private void RefreshSentenceItems()
    {
        SentenceAudioItems.Clear();
        SetCurrentSentence(null);

        var sentences = SentenceSplitter.Split(SelectedOutputText);
        for (var i = 0; i < sentences.Count; i++)
        {
            SentenceAudioItems.Add(new SentenceAudioItemViewModel(i, sentences[i]));
        }

        SelectedSentenceAudioItem = SentenceAudioItems.FirstOrDefault();
        AppLogger.Info($"Sentence items refreshed. Count={SentenceAudioItems.Count}, SelectedStyle={SelectedStyle}.");
        NotifySentenceControlProperties();
    }

    private void InvalidateSentenceAudio()
    {
        _isSentencePlaybackActive = false;
        foreach (var sentence in SentenceAudioItems)
        {
            sentence.AudioFilePath = "";
            sentence.IsAudioReady = false;
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
        Dispatcher.UIThread.Post(async () =>
        {
            if (_isSentencePlaybackActive && IsSentenceLoopMode())
            {
                _audioPlaybackService.Play();
                StatusText = $"正在循环第 {CurrentSentenceAudioItem?.Index + 1 ?? 1} 句。";
            }
            else if (_isSentencePlaybackActive && ShouldAutoPlayNextSentence())
            {
                await PlayNextSentenceFromPlaybackEndAsync();
            }
            else if (ShouldLoopWholeAudio())
            {
                _audioPlaybackService.Play();
                StatusText = "正在循环整段。";
            }
            else
            {
                _isSentencePlaybackActive = false;
                StatusText = "播放完成。";
                AppLogger.Info("Playback completed.");
            }

            UpdatePlaybackProgress();
            UpdateAudioStateProperties();
        });
    }

    private bool ShouldLoopWholeAudio()
    {
        return string.Equals(SelectedWholeAudioPlaybackMode, "整段循环", StringComparison.Ordinal);
    }

    private bool ShouldAutoPlayNextSentence()
    {
        return string.Equals(SelectedSentencePlaybackMode, "自动下一句", StringComparison.Ordinal);
    }

    private bool IsSentenceLoopMode()
    {
        return string.Equals(SelectedSentencePlaybackMode, "单句循环", StringComparison.Ordinal);
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
        NotifySentenceControlProperties();
    }

    private void NotifySentenceControlProperties()
    {
        OnPropertyChanged(nameof(SentencePlayButtonText));
        OnPropertyChanged(nameof(SentencePlayIcon));
        OnPropertyChanged(nameof(CanPlaySelectedSentence));
        OnPropertyChanged(nameof(CanPlayPreviousSentence));
        OnPropertyChanged(nameof(CanPlayNextSentence));
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
            AppLogger.Error(ex, $"Operation failed. BusyText={busyText}");
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
            AppLogger.Info($"History loaded. Count={HistoryItems.Count}.");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to load history.");
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

    private static ITextToSpeechService CreateTtsService()
    {
        var config = LocalAppConfig.Load();
        var appDataDirectory = GetAppDataDirectory();
        var googleTts = new GoogleTextToSpeechService(
            new HttpClient { Timeout = TimeSpan.FromSeconds(120) },
            config.GoogleTtsApiKey,
            Path.Combine(appDataDirectory, "audio-temp"));

        return new TtsAudioCacheService(
            googleTts,
            Path.Combine(appDataDirectory, "audio-cache"),
            "GoogleCloudTextToSpeech");
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
        TypeDisplay = LocalizeType(issue.Type);
        Severity = issue.Severity;
        Original = issue.Original;
        Suggestion = issue.Suggestion;
        ExplanationZh = issue.ExplanationZh;
        JlptLevel = issue.JlptLevel;
        ExampleJapanese = issue.ExampleJapanese;
        ExampleChinese = issue.ExampleChinese;
    }

    public string Type { get; }
    public string TypeDisplay { get; }
    public string Severity { get; }
    public string Original { get; }
    public string Suggestion { get; }
    public string ExplanationZh { get; }
    public string JlptLevel { get; }
    public string ExampleJapanese { get; }
    public string ExampleChinese { get; }

    private static string LocalizeType(string type) => type switch
    {
        "Particle" => "助词",
        "VerbConjugation" => "动词变形",
        "Tense" => "时态",
        "Politeness" => "礼貌表达",
        "Ambiguity" => "歧义",
        "WordChoice" => "用词选择",
        "Collocation" => "搭配",
        "ChineseLikeExpression" => "中文式表达",
        "Naturalness" => "自然度",
        "Punctuation" => "标点",
        "Other" => "其他",
        _ => string.IsNullOrWhiteSpace(type) ? "问题" : type
    };
}

public sealed partial class SentenceAudioItemViewModel : ObservableObject
{
    public SentenceAudioItemViewModel(int index, string text)
    {
        Index = index;
        Text = text;
    }

    public int Index { get; }
    public string Text { get; }
    public string IndexText => $"{Index + 1}.";
    public string CurrentMarker => IsCurrent ? "▶" : "";
    public string CurrentBackground => IsCurrent ? "#EFF6FF" : "Transparent";
    public string CurrentBorderBrush => IsCurrent ? "#2563EB" : "Transparent";
    public string CurrentAccentBackground => IsCurrent ? "#2563EB" : "Transparent";
    public string CurrentIndexBackground => IsCurrent ? "#2563EB" : "#EEF2F6";
    public string CurrentIndexForeground => IsCurrent ? "#FFFFFF" : "#697586";
    public string CurrentTextForeground => IsCurrent ? "#0F172A" : "#172033";
    public string AudioStatusText => IsCurrent ? "当前句" : IsAudioReady ? "已缓存" : "";
    public string AudioStatusForeground => IsCurrent ? "#1D4ED8" : "#116149";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentMarker))]
    [NotifyPropertyChangedFor(nameof(CurrentBackground))]
    [NotifyPropertyChangedFor(nameof(CurrentBorderBrush))]
    [NotifyPropertyChangedFor(nameof(CurrentAccentBackground))]
    [NotifyPropertyChangedFor(nameof(CurrentIndexBackground))]
    [NotifyPropertyChangedFor(nameof(CurrentIndexForeground))]
    [NotifyPropertyChangedFor(nameof(CurrentTextForeground))]
    [NotifyPropertyChangedFor(nameof(AudioStatusText))]
    [NotifyPropertyChangedFor(nameof(AudioStatusForeground))]
    private bool _isCurrent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AudioStatusText))]
    [NotifyPropertyChangedFor(nameof(AudioStatusForeground))]
    private bool _isAudioReady;

    [ObservableProperty]
    private string _audioFilePath = "";
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
