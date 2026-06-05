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
    private static LocalAppConfig CurrentConfig = LocalAppConfig.Load();

    private readonly ITextToSpeechService _textToSpeechService;
    private readonly HistoryStore _historyStore;
    private IAnalysisClient _analysisClient;
    private JapaneseAnalysisResult? _latestResult;
    private string? _latestAudioFilePath;
    private string? _latestAudioSourceText;
    private string? _latestAudioVoiceName;
    private double? _latestAudioSpeakingRate;

    public MainWindowViewModel()
        : this(CreateAnalysisClient(CurrentConfig), CreateTtsService(CurrentConfig), CreateHistoryStore(), new NAudioPlaybackService())
    {
    }

    public AnalysisSettingsViewModel AnalysisSettings { get; }
    public PlaybackController Playback { get; }

    public MainWindowViewModel(
        IAnalysisClient analysisClient,
        ITextToSpeechService textToSpeechService,
        HistoryStore historyStore,
        IAudioPlaybackService audioPlaybackService)
    {
        AnalysisSettings = new AnalysisSettingsViewModel(
            CurrentConfig,
            (newConfig, newClient) =>
            {
                CurrentConfig = newConfig;
                _analysisClient = newClient;
            },
            status => StatusText = status,
            RunBusyAsync);
        Playback = new PlaybackController(audioPlaybackService);
        Playback.PlaybackEnded += OnPlaybackEnded;
        Playback.PropertyChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(PlayAudioButtonText));
            OnPropertyChanged(nameof(PlayAudioIcon));
            NotifySentenceControlProperties();
            NotifyPrimaryPlaybackProperties();
        };

        _analysisClient = analysisClient;
        _textToSpeechService = textToSpeechService;
        _historyStore = historyStore;

        AppLogger.Info("MainWindowViewModel initialized.");
        _ = LoadHistoryAsync();
    }

    public string[] LanguageModes { get; } = ["自动判断", "中文", "日语"];
    public string[] Scenarios { get; } = ["日常学习", "作文修改", "口语表达", "邮件/商务", "自我介绍", "JLPT 练习"];
    public string[] Styles { get; } = ["修正版", "自然版", "朗读版"];
    public string[] Voices { get; } = ["ja-JP-Neural2-B", "ja-JP-Neural2-C", "ja-JP-Wavenet-B", "ja-JP-Wavenet-C"];
    public string[] PlaybackScopes { get; } = ["整段", "逐句"];
    public string[] WholeAudioPlaybackModes { get; } = ["整段播放一次", "整段循环"];
    public string[] SentencePlaybackModes { get; } = ["手动逐句", "自动下一句", "单句循环"];

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private string _selectedLanguageMode = "自动判断";

    [ObservableProperty]
    private string _selectedScenario = "日常学习";

    [ObservableProperty]
    private string _selectedStyle = "自然版";

    [ObservableProperty]
    private string _selectedVoice = CurrentConfig.GoogleTtsVoiceName;

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
    private string _selectedWholeAudioPlaybackMode = "整段播放一次";

    [ObservableProperty]
    private string _selectedPlaybackScope = "整段";

    public string PlayAudioButtonText => Playback.CurrentScope == PlaybackScope.Whole && Playback.IsPlaying ? "暂停" : Playback.CurrentScope == PlaybackScope.Whole && Playback.IsPaused ? "继续" : "播放";
    public string PlayAudioIcon => Playback.CurrentScope == PlaybackScope.Whole && Playback.IsPlaying ? "⏸" : "▶";
    public string SentencePlayButtonText => Playback.CurrentScope == PlaybackScope.Sentence && Playback.IsPlaying ? "暂停当前句" : Playback.CurrentScope == PlaybackScope.Sentence && Playback.IsPaused ? "继续当前句" : "播放当前句";
    public string SentencePlayIcon => Playback.CurrentScope == PlaybackScope.Sentence && Playback.IsPlaying ? "⏸" : "▶";
    public bool IsWholePlaybackScope => string.Equals(SelectedPlaybackScope, "整段", StringComparison.Ordinal);
    public bool IsSentencePlaybackScope => string.Equals(SelectedPlaybackScope, "逐句", StringComparison.Ordinal);
    public string PrimaryPlaybackIcon => IsSentencePlaybackScope ? SentencePlayIcon : PlayAudioIcon;
    public string PrimaryPlaybackButtonText => IsSentencePlaybackScope ? SentencePlayButtonText : PlayAudioButtonText;
    public bool CanUsePrimaryPlayback => IsSentencePlaybackScope ? CanPlaySelectedSentence : !IsBusy;
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
    private HistoryItemViewModel? _selectedHistoryItem;

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

        _analysisClient = AnalysisSettings.CreateClient();
        await RunBusyAsync($"正在调用 {_analysisClient.ProviderName} 分析文本...", async cancellationToken =>
        {
            AppLogger.Info($"Analysis started. TextLength={InputText.Trim().Length}, LanguageMode={SelectedLanguageMode}, Scenario={SelectedScenario}, TargetStyle={SelectedStyle}.");
            var request = new AnalyzeTextRequest
            {
                Text = InputText.Trim(),
                LanguageMode = ParseLanguageMode(SelectedLanguageMode),
                Scenario = SelectedScenario,
                TargetStyle = SelectedStyle
            };

            var result = await _analysisClient.AnalyzeAsync(request, cancellationToken);
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
        AppLogger.Info($"Whole audio play requested. SelectedStyle={SelectedStyle}, VoiceName={SelectedVoice}, SpeakingRate={SpeakingRate}, Mode={SelectedWholeAudioPlaybackMode}.");
        var text = GetTextForSelectedStyle();
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText = "请先分析文本，或选择一个有内容的输出版本。";
            return;
        }

        if (HasPlayableAudioFor(text))
        {
            Playback.Load(_latestAudioFilePath!, PlaybackScope.Whole);
            Playback.Toggle();
            return;
        }

        await RunBusyAsync("语音生成中...", async cancellationToken =>
        {
            await GenerateSpeechForCurrentSelectionAsync(text, cancellationToken);
            Playback.Load(_latestAudioFilePath!, PlaybackScope.Whole);
            Playback.Play();
            StatusText = "正在播放。";
        });
    }

    [RelayCommand]
    private void StopAudio()
    {
        AppLogger.Info("Stop audio requested.");
        Playback.Stop();
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

    [RelayCommand]
    private async Task PrimaryPlaybackAsync()
    {
        if (IsSentencePlaybackScope)
        {
            await PlaySelectedSentenceAsync();
            return;
        }

        await PlayAudioAsync();
    }

    [RelayCommand]
    private void RestoreHistory(HistoryItemViewModel? historyItem)
    {
        historyItem ??= SelectedHistoryItem;
        if (historyItem is null)
        {
            StatusText = "请选择一条历史记录。";
            return;
        }

        AppLogger.Info($"History restore requested. CreatedAt={historyItem.CreatedAt:O}, OriginalTextLength={historyItem.OriginalText.Length}.");
        Playback.Stop();
        InputText = historyItem.OriginalText;
        ApplyAnalysisResult(historyItem.AnalysisResult);
        SelectedHistoryItem = historyItem;
        StatusText = $"已恢复 {historyItem.CreatedAt:yyyy-MM-dd HH:mm} 的历史记录。";
    }

    partial void OnSelectedStyleChanged(string value)
    {
        Playback.Stop();
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


    partial void OnIsBusyChanged(bool value)
    {
        NotifySentenceControlProperties();
        NotifyPrimaryPlaybackProperties();
    }

    partial void OnSelectedSentenceAudioItemChanged(SentenceAudioItemViewModel? value)
    {
        NotifySentenceControlProperties();
        NotifyPrimaryPlaybackProperties();
    }

    partial void OnSelectedPlaybackScopeChanged(string value)
    {
        NotifyPlaybackScopeProperties();
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
            "朗读版" => JapaneseStyle.ReadingOptimized,
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
        // Removed, PlaybackController handles loading directly.
    }

    private async Task PlaySentenceAsync(SentenceAudioItemViewModel sentence, CancellationToken cancellationToken)
    {
        if (CurrentSentenceAudioItem == sentence
            && !string.IsNullOrWhiteSpace(sentence.AudioFilePath)
            && string.Equals(Playback.LoadedFilePath, sentence.AudioFilePath, StringComparison.Ordinal)
            && (Playback.IsPlaying || Playback.IsPaused))
        {
            Playback.CurrentScope = PlaybackScope.Sentence;
            Playback.Toggle();
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
            Playback.Load(sentence.AudioFilePath, PlaybackScope.Sentence);
            Playback.Play();
            StatusText = $"正在播放第 {sentence.Index + 1} 句。";
            AppLogger.Info($"Sentence playback started. Index={sentence.Index}, Mode={SelectedSentencePlaybackMode}.");
        });
    }

    private async Task PlayNextSentenceFromPlaybackEndAsync()
    {
        var next = GetRelativeSentence(1);
        if (next is null)
        {
            Playback.CurrentScope = PlaybackScope.None;
            StatusText = "逐句播放完成。";
            AppLogger.Info("Sentence playback completed at final sentence.");
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
        Playback.Stop();
        foreach (var sentence in SentenceAudioItems)
        {
            sentence.AudioFilePath = "";
            sentence.IsAudioReady = false;
        }
    }

    private void InvalidateCurrentAudio()
    {
        Playback.Stop();
        _latestAudioFilePath = null;
        _latestAudioSourceText = null;
        _latestAudioVoiceName = null;
        _latestAudioSpeakingRate = null;
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        if (Playback.CurrentScope == PlaybackScope.Sentence && IsSentenceLoopMode())
        {
            Playback.Play();
            StatusText = $"正在循环第 {CurrentSentenceAudioItem?.Index + 1 ?? 1} 句。";
        }
        else if (Playback.CurrentScope == PlaybackScope.Sentence && ShouldAutoPlayNextSentence())
        {
            _ = PlayNextSentenceFromPlaybackEndAsync();
        }
        else if (Playback.CurrentScope == PlaybackScope.Whole && ShouldLoopWholeAudio())
        {
            Playback.Play();
            StatusText = "正在循环整段。";
        }
        else
        {
            Playback.CurrentScope = PlaybackScope.None;
            StatusText = "播放完成。";
            AppLogger.Info("Playback completed.");
        }
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

    private void NotifySentenceControlProperties()
    {
        OnPropertyChanged(nameof(SentencePlayButtonText));
        OnPropertyChanged(nameof(SentencePlayIcon));
        OnPropertyChanged(nameof(CanPlaySelectedSentence));
        OnPropertyChanged(nameof(CanPlayPreviousSentence));
        OnPropertyChanged(nameof(CanPlayNextSentence));
        NotifyPrimaryPlaybackProperties();
    }

    private void NotifyPlaybackScopeProperties()
    {
        OnPropertyChanged(nameof(IsWholePlaybackScope));
        OnPropertyChanged(nameof(IsSentencePlaybackScope));
        NotifyPrimaryPlaybackProperties();
    }

    private void NotifyPrimaryPlaybackProperties()
    {
        OnPropertyChanged(nameof(PrimaryPlaybackIcon));
        OnPropertyChanged(nameof(PrimaryPlaybackButtonText));
        OnPropertyChanged(nameof(CanUsePrimaryPlayback));
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

    private static IAnalysisClient CreateAnalysisClient(LocalAppConfig config)
    {
        return AnalysisClientFactory.Create(config);
    }

    private static ITextToSpeechService CreateTtsService(LocalAppConfig config)
    {
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
