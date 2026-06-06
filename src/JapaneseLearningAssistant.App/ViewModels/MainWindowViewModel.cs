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

    private readonly PromptProfileManager _promptProfileManager = new();
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
    public SentencePlaybackViewModel SentencePlayback { get; }

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
        SentencePlayback = new SentencePlaybackViewModel(
            textToSpeechService,
            Playback,
            () => SelectedVoice,
            () => SpeakingRate,
            status => StatusText = status,
            RunBusyAsync);

        Playback.PlaybackEnded += OnPlaybackEnded;
        Playback.PropertyChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(PlayAudioButtonText));
            OnPropertyChanged(nameof(PlayAudioIcon));
            OnPropertyChanged(nameof(SentencePlayButtonText));
            OnPropertyChanged(nameof(SentencePlayIcon));
            NotifySentenceControlProperties();
            NotifyPrimaryPlaybackProperties();
        };
        SentencePlayback.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SentencePlaybackViewModel.CanPlaySelected) ||
                e.PropertyName == nameof(SentencePlaybackViewModel.CanPlayPrevious) ||
                e.PropertyName == nameof(SentencePlaybackViewModel.CanPlayNext))
            {
                NotifySentenceControlProperties();
            }
        };

        _analysisClient = analysisClient;
        _textToSpeechService = textToSpeechService;
        _historyStore = historyStore;

        AppLogger.Info("MainWindowViewModel initialized.");
        foreach (var profile in _promptProfileManager.LoadProfiles())
        {
            PromptProfiles.Add(profile);
        }
        SelectedPromptProfile = PromptProfiles.FirstOrDefault();

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
    public bool CanPlaySelectedSentence => !IsBusy && SentencePlayback.CanPlaySelected;
    public bool CanPlayPreviousSentence => !IsBusy && SentencePlayback.CanPlayPrevious;
    public bool CanPlayNextSentence => !IsBusy && SentencePlayback.CanPlayNext;

    public ObservableCollection<IssueViewModel> Issues { get; } = [];
    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; } = [];
    public ObservableCollection<PromptProfile> PromptProfiles { get; } = [];

    [ObservableProperty]
    private PromptProfile? _selectedPromptProfile;

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
                TargetStyle = SelectedStyle,
                PromptTemplate = SelectedPromptProfile?.Template ?? ""
            };

            var result = await _analysisClient.AnalyzeAsync(request, cancellationToken);
            ApplyAnalysisResult(result);
            AppLogger.Info($"Analysis completed. Issues={result.Issues.Count}, SelectedStyle={SelectedStyle}, SentenceCount={SentencePlayback.Items.Count}.");

            await _historyStore.SaveAsync(new HistoryEntry
            {
                CreatedAt = DateTimeOffset.Now,
                OriginalText = InputText,
                AnalysisProvider = AnalysisSettings.SelectedProvider,
                AnalysisModel = AnalysisSettings.Model,
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
    private async Task PrimaryPlaybackAsync()
    {
        if (IsSentencePlaybackScope)
        {
            await SentencePlayback.PlaySelectedCommand.ExecuteAsync(null);
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

    private bool HasPlayableAudioFor(string text)
    {
        return !string.IsNullOrWhiteSpace(_latestAudioFilePath)
            && File.Exists(_latestAudioFilePath)
            && string.Equals(_latestAudioSourceText, text, StringComparison.Ordinal)
            && string.Equals(_latestAudioVoiceName, SelectedVoice, StringComparison.Ordinal)
            && _latestAudioSpeakingRate.HasValue
            && Math.Abs(_latestAudioSpeakingRate.Value - SpeakingRate) < 0.001;
    }

    private void RefreshSentenceItems()
    {
        SentencePlayback.RefreshItems(SelectedOutputText);
    }

    private void InvalidateSentenceAudio()
    {
        SentencePlayback.InvalidateAudio();
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
        if (Playback.CurrentScope == PlaybackScope.Whole)
        {
            if (ShouldLoopWholeAudio())
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
    }

    private bool ShouldLoopWholeAudio()
    {
        return string.Equals(SelectedWholeAudioPlaybackMode, "整段循环", StringComparison.Ordinal);
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
