using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private CancellationTokenSource? _busyCancellationSource;

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
        AnalysisSettings.PropertyChanged += OnAnalysisSettingsPropertyChanged;
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
    public string[] Scenarios { get; } = ["简体", "敬语", "商务"];
    public string[] Styles { get; } =
    [
        "直译：对应中文",
        "纠错：保留原句",
        "润色：地道自然",
        "普通体：朋友日记",
        "丁寧語：一般交流",
        "商务敬语",
        "跟读：适合朗读"
    ];
    public string[] Voices { get; } = ["ja-JP-Neural2-B", "ja-JP-Neural2-C", "ja-JP-Wavenet-B", "ja-JP-Wavenet-C"];
    public string[] PlaybackScopes { get; } = ["整段", "逐句"];
    public string[] WholeAudioPlaybackModes { get; } = ["整段播放一次", "整段循环"];
    public string[] SentencePlaybackModes { get; } = ["手动逐句", "自动下一句", "单句循环"];

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private string _selectedLanguageMode = "自动判断";

    [ObservableProperty]
    private string _selectedScenario = "敬语";

    [ObservableProperty]
    private string _selectedStyle = "润色：地道自然";

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
    public string SelectedStyleDescription => SelectedStyle switch
    {
        "直译：对应中文" => "用于对照中文原意：尽量保留原文信息，帮助理解中文到日语的对应关系。",
        "纠错：保留原句" => "用于看哪里错了：尽量保留你的原句结构，只修明显错误和不自然的地方。",
        "跟读：适合朗读" => "用于听和跟读：基于自然表达拆分句子，让 TTS 和逐句练习更顺。",
        "普通体：朋友日记" => "用于朋友、日记和轻松口语场景：表达更自然，但不使用丁寧語。",
        "丁寧語：一般交流" => "用于一般礼貌交流：适合老师、同事、初次见面等常见场景。",
        "商务敬语" => "用于邮件、客户沟通和更正式场景：更重视礼貌层级和措辞稳妥。",
        _ => "用于直接使用：改成更像日本人实际会说、会写的自然表达。"
    };
    public bool CanUsePrimaryPlayback => IsSentencePlaybackScope ? CanPlaySelectedSentence : !IsBusy && HasSelectedOutput;
    public bool CanPlaySelectedSentence => !IsBusy && SentencePlayback.CanPlaySelected;
    public bool CanPlayPreviousSentence => !IsBusy && SentencePlayback.CanPlayPrevious;
    public bool CanPlayNextSentence => !IsBusy && SentencePlayback.CanPlayNext;
    public bool CanCancelBusy => IsBusy;
    public bool HasInputText => !string.IsNullOrWhiteSpace(InputText);
    public bool HasSelectedOutput => !string.IsNullOrWhiteSpace(SelectedOutputText);
    public bool CanAnalyzeInput => !IsBusy && HasInputText;
    public bool CanReadInput => !IsBusy && HasInputText;
    public string InputStatsText => HasInputText ? $"{InputText.Trim().Length} 字符" : "还没有输入内容";
    public string InputHintText => HasInputText ? "可直接朗读日语，或让 AI 分析并优化。" : "粘贴中文可生成自然日语；粘贴日语可检查语法、自然度和敬语。";
    public string ActiveAnalysisConfigText => $"{AnalysisSettings.SelectedProvider} / {AnalysisSettings.Model}";
    public string AnalysisConfigStatusText => string.IsNullOrWhiteSpace(AnalysisSettings.ApiKey) ? "AI Key 未配置" : "AI Key 已配置";
    public string TtsConfigStatusText => string.IsNullOrWhiteSpace(CurrentConfig.GoogleTtsApiKey) ? "TTS Key 未配置" : "TTS Key 已配置";
    public string ActiveVoiceText => $"{SelectedVoice} · {SpeakingRate:F2}x";

    public ObservableCollection<IssueViewModel> Issues { get; } = [];
    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; } = [];
    public ObservableCollection<PromptProfile> PromptProfiles { get; } = [];

    [ObservableProperty]
    private PromptProfile? _selectedPromptProfile;

    [ObservableProperty]
    private HistoryItemViewModel? _selectedHistoryItem;

    [ObservableProperty]
    private bool _isCompactLayout;

    [RelayCommand(CanExecute = nameof(CanAnalyzeInput))]
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

    [RelayCommand(CanExecute = nameof(CanUsePrimaryPlayback))]
    private async Task PlayAudioAsync()
    {
        SentencePlayback.CancelPendingPlayback();
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

    [RelayCommand(CanExecute = nameof(CanReadInput))]
    private async Task ReadInputJapaneseAsync()
    {
        SentencePlayback.CancelPendingPlayback();
        Playback.Stop();

        var text = InputText.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText = "请输入要朗读的日语文本。";
            return;
        }

        AppLogger.Info($"Direct Japanese reading requested. TextLength={text.Length}, VoiceName={SelectedVoice}, SpeakingRate={SpeakingRate}.");
        if (HasPlayableAudioFor(text))
        {
            Playback.Load(_latestAudioFilePath!, PlaybackScope.Whole);
            Playback.Toggle();
            return;
        }

        await RunBusyAsync("正在生成输入文本的朗读音频...", async cancellationToken =>
        {
            await GenerateSpeechForCurrentSelectionAsync(text, cancellationToken);
            Playback.Load(_latestAudioFilePath!, PlaybackScope.Whole);
            Playback.Play();
            StatusText = "正在朗读输入文本。";
        });
    }

    [RelayCommand]
    private void StopAudio()
    {
        AppLogger.Info("Stop audio requested.");
        SentencePlayback.CancelPendingPlayback();
        Playback.Stop();
        StatusText = "播放已停止。";
    }

    [RelayCommand(CanExecute = nameof(CanUsePrimaryPlayback))]
    private async Task PrimaryPlaybackAsync()
    {
        if (IsSentencePlaybackScope)
        {
            await SentencePlayback.PlaySelectedCommand.ExecuteAsync(null);
            return;
        }

        await PlayAudioAsync();
    }

    [RelayCommand(CanExecute = nameof(CanCancelBusy))]
    private void CancelBusy()
    {
        if (_busyCancellationSource is null)
        {
            return;
        }

        _busyCancellationSource.Cancel();
        SentencePlayback.CancelPendingPlayback();
        StatusText = "正在取消当前操作...";
        AppLogger.Info("Busy operation cancellation requested.");
    }

    [RelayCommand]
    private void RestoreHistory(HistoryItemViewModel? historyItem)
    {
        if (IsBusy)
        {
            StatusText = "当前操作完成后再恢复历史记录。";
            return;
        }

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
        OnPropertyChanged(nameof(SelectedStyleDescription));
        NotifyPrimaryPlaybackProperties();
    }

    partial void OnSelectedOutputTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSelectedOutput));
        NotifyPrimaryPlaybackProperties();
    }

    partial void OnSelectedVoiceChanged(string value)
    {
        InvalidateCurrentAudio();
        InvalidateSentenceAudio();
        OnPropertyChanged(nameof(ActiveVoiceText));
    }

    partial void OnSpeakingRateChanged(double value)
    {
        InvalidateCurrentAudio();
        InvalidateSentenceAudio();
        OnPropertyChanged(nameof(ActiveVoiceText));
    }


    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCancelBusy));
        OnPropertyChanged(nameof(CanAnalyzeInput));
        OnPropertyChanged(nameof(CanReadInput));
        NotifySentenceControlProperties();
        NotifyPrimaryPlaybackProperties();
        AnalyzeCommand.NotifyCanExecuteChanged();
        ReadInputJapaneseCommand.NotifyCanExecuteChanged();
        PlayAudioCommand.NotifyCanExecuteChanged();
        PrimaryPlaybackCommand.NotifyCanExecuteChanged();
        CancelBusyCommand.NotifyCanExecuteChanged();
    }

    partial void OnInputTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasInputText));
        OnPropertyChanged(nameof(CanAnalyzeInput));
        OnPropertyChanged(nameof(CanReadInput));
        OnPropertyChanged(nameof(InputStatsText));
        OnPropertyChanged(nameof(InputHintText));
        AnalyzeCommand.NotifyCanExecuteChanged();
        ReadInputJapaneseCommand.NotifyCanExecuteChanged();
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
        OnPropertyChanged(nameof(HasSelectedOutput));
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
            "直译：对应中文" => (JapaneseStyle?)null,
            "纠错：保留原句" => JapaneseStyle.Corrected,
            "润色：地道自然" => JapaneseStyle.Natural,
            "普通体：朋友日记" => JapaneseStyle.Plain,
            "丁寧語：一般交流" => JapaneseStyle.Polite,
            "商务敬语" => JapaneseStyle.BusinessKeigo,
            "跟读：适合朗读" => JapaneseStyle.ReadingOptimized,
            _ => JapaneseStyle.Natural
        };

        return style is null ? _latestResult.TranslatedJapanese : _latestResult.GetTextForStyle(style.Value);
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
        OnPropertyChanged(nameof(HasSelectedOutput));
        PlayAudioCommand.NotifyCanExecuteChanged();
        PrimaryPlaybackCommand.NotifyCanExecuteChanged();
    }

    private async Task RunBusyAsync(string busyText, Func<CancellationToken, Task> operation)
    {
        IsBusy = true;
        StatusText = busyText;
        using var userCancellation = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(userCancellation.Token, timeout.Token);
        _busyCancellationSource = userCancellation;
        try
        {
            await operation(linkedCancellation.Token);
        }
        catch (OperationCanceledException) when (userCancellation.IsCancellationRequested)
        {
            AppLogger.Info($"Operation cancelled by user. BusyText={busyText}");
            StatusText = "操作已取消。";
        }
        catch (OperationCanceledException)
        {
            AppLogger.Info($"Operation timed out. BusyText={busyText}");
            StatusText = "操作超时，请稍后重试或缩短文本。";
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Operation failed. BusyText={busyText}");
            StatusText = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_busyCancellationSource, userCancellation))
            {
                _busyCancellationSource = null;
            }

            IsBusy = false;
        }
    }

    private void OnAnalysisSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AnalysisSettings.SelectedProvider) or nameof(AnalysisSettings.Model) or nameof(AnalysisSettings.ApiKey))
        {
            OnPropertyChanged(nameof(ActiveAnalysisConfigText));
            OnPropertyChanged(nameof(AnalysisConfigStatusText));
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
                HistoryItems.Add(new HistoryItemViewModel(entry, RestoreHistory));
            }
            AppLogger.Info($"History loaded. Count={HistoryItems.Count}.");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to load history.");
            StatusText = $"历史记录读取失败：{ex.Message}";
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
