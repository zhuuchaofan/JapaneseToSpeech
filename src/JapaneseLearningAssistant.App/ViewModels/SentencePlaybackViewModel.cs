using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JapaneseLearningAssistant.Core.Models;
using JapaneseLearningAssistant.Core.Services;
using JapaneseLearningAssistant.App.Services;

namespace JapaneseLearningAssistant.App.ViewModels;

public partial class SentencePlaybackViewModel : ObservableObject
{
    private readonly ITextToSpeechService _textToSpeechService;
    private readonly PlaybackController _playbackController;
    private readonly Func<string> _getVoiceName;
    private readonly Func<double> _getSpeakingRate;
    private readonly Action<string> _setStatusText;
    private readonly Func<string, Func<CancellationToken, Task>, Task> _runBusyAsync;
    private int _playbackSessionVersion;

    [ObservableProperty]
    private SentenceAudioItemViewModel? _selectedSentence;

    [ObservableProperty]
    private SentenceAudioItemViewModel? _currentSentence;

    [ObservableProperty]
    private string _playbackMode = "自动下一句";

    public ObservableCollection<SentenceAudioItemViewModel> Items { get; } = [];

    public string[] PlaybackModes { get; } = ["手动逐句", "自动下一句", "单句循环"];

    public SentencePlaybackViewModel(
        ITextToSpeechService textToSpeechService,
        PlaybackController playbackController,
        Func<string> getVoiceName,
        Func<double> getSpeakingRate,
        Action<string> setStatusText,
        Func<string, Func<CancellationToken, Task>, Task> runBusyAsync)
    {
        _textToSpeechService = textToSpeechService;
        _playbackController = playbackController;
        _getVoiceName = getVoiceName;
        _getSpeakingRate = getSpeakingRate;
        _setStatusText = setStatusText;
        _runBusyAsync = runBusyAsync;

        _playbackController.PlaybackEnded += OnPlaybackEnded;
    }

    public bool CanPlaySelected => Items.Count > 0;
    public bool CanPlayPrevious => (CurrentSentence ?? SelectedSentence)?.Index > 0;
    public bool CanPlayNext => (CurrentSentence ?? SelectedSentence)?.Index < Items.Count - 1;

    [RelayCommand]
    public async Task PlaySelectedAsync()
    {
        var sentence = SelectedSentence ?? CurrentSentence ?? Items.FirstOrDefault();
        if (sentence is null)
        {
            _setStatusText("请先分析文本，生成可逐句播放的内容。");
            return;
        }

        AppLogger.Info($"Selected sentence play requested. Index={sentence.Index}, TextLength={sentence.Text.Length}, Mode={PlaybackMode}.");
        await PlaySentenceAsync(sentence, CancellationToken.None, startNewSession: true);
    }

    [RelayCommand]
    public async Task PlayPreviousAsync()
    {
        var sentence = GetRelativeSentence(-1);
        if (sentence is null)
        {
            _setStatusText("已经是第一句。");
            return;
        }

        await PlaySentenceAsync(sentence, CancellationToken.None, startNewSession: true);
    }

    [RelayCommand]
    public async Task PlayNextAsync()
    {
        var sentence = GetRelativeSentence(1);
        if (sentence is null)
        {
            _setStatusText("已经是最后一句。");
            return;
        }

        await PlaySentenceAsync(sentence, CancellationToken.None, startNewSession: true);
    }

    public void RefreshItems(string text)
    {
        CancelPendingPlayback();
        Items.Clear();
        SetCurrentSentence(null);

        var sentences = SentenceSplitter.Split(text);
        for (var i = 0; i < sentences.Count; i++)
        {
            Items.Add(new SentenceAudioItemViewModel(i, sentences[i], sentence => PlaySentenceAsync(sentence, CancellationToken.None, startNewSession: true)));
        }

        SelectedSentence = Items.FirstOrDefault();
        AppLogger.Info($"Sentence items refreshed. Count={Items.Count}.");
        NotifyControlProperties();
    }

    public void InvalidateAudio()
    {
        CancelPendingPlayback();
        if (_playbackController.CurrentScope == PlaybackScope.Sentence)
        {
            _playbackController.Stop();
        }

        foreach (var sentence in Items)
        {
            sentence.AudioFilePath = "";
            sentence.IsAudioReady = false;
        }
    }

    public void CancelPendingPlayback()
    {
        Interlocked.Increment(ref _playbackSessionVersion);
    }

    public async Task PlaySentenceAsync(SentenceAudioItemViewModel sentence, CancellationToken cancellationToken, bool startNewSession = true)
    {
        if (CurrentSentence == sentence
            && !string.IsNullOrWhiteSpace(sentence.AudioFilePath)
            && string.Equals(_playbackController.LoadedFilePath, sentence.AudioFilePath, StringComparison.Ordinal)
            && (_playbackController.IsPlaying || _playbackController.IsPaused))
        {
            _playbackController.CurrentScope = PlaybackScope.Sentence;
            _playbackController.Toggle();
            return;
        }

        var sessionVersion = startNewSession
            ? Interlocked.Increment(ref _playbackSessionVersion)
            : Volatile.Read(ref _playbackSessionVersion);

        await _runBusyAsync($"正在准备第 {sentence.Index + 1} 句语音...", async token =>
        {
            await GenerateSpeechAsync(sentence, token);
            if (!IsCurrentSession(sessionVersion))
            {
                AppLogger.Info($"Ignored stale sentence playback task after audio generation. Index={sentence.Index}, Session={sessionVersion}.");
                return;
            }

            if (string.IsNullOrWhiteSpace(sentence.AudioFilePath))
            {
                throw new FileNotFoundException("句子音频文件不存在。", sentence.AudioFilePath);
            }

            _playbackController.Load(sentence.AudioFilePath, PlaybackScope.Sentence);
            if (!IsCurrentSession(sessionVersion))
            {
                AppLogger.Info($"Ignored stale sentence playback task after audio load. Index={sentence.Index}, Session={sessionVersion}.");
                return;
            }

            SetCurrentSentence(sentence);
            _playbackController.Play();
            _setStatusText($"正在播放第 {sentence.Index + 1} 句。");
            AppLogger.Info($"Sentence playback started. Index={sentence.Index}, Mode={PlaybackMode}.");
        });
    }

    private async Task GenerateSpeechAsync(SentenceAudioItemViewModel sentence, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sentence.AudioFilePath) && File.Exists(sentence.AudioFilePath))
        {
            return;
        }

        var audio = await _textToSpeechService.GenerateAsync(new TtsRequest
        {
            Text = sentence.Text,
            VoiceName = _getVoiceName(),
            SpeakingRate = _getSpeakingRate()
        }, cancellationToken);

        sentence.AudioFilePath = audio.FilePath;
        sentence.IsAudioReady = true;
        AppLogger.Info($"Sentence audio prepared. Index={sentence.Index}, FilePath={audio.FilePath}, VoiceName={audio.VoiceName}.");
    }

    private SentenceAudioItemViewModel? GetRelativeSentence(int offset)
    {
        var current = CurrentSentence ?? SelectedSentence ?? Items.FirstOrDefault();
        if (current is null)
        {
            return null;
        }

        var nextIndex = current.Index + offset;
        return Items.FirstOrDefault(item => item.Index == nextIndex);
    }

    private void SetCurrentSentence(SentenceAudioItemViewModel? sentence)
    {
        if (CurrentSentence is not null)
        {
            CurrentSentence.IsCurrent = false;
        }

        CurrentSentence = sentence;
        SelectedSentence = sentence;

        if (CurrentSentence is not null)
        {
            CurrentSentence.IsCurrent = true;
        }

        NotifyControlProperties();
    }

    partial void OnSelectedSentenceChanged(SentenceAudioItemViewModel? value)
    {
        NotifyControlProperties();
    }

    private void NotifyControlProperties()
    {
        OnPropertyChanged(nameof(CanPlaySelected));
        OnPropertyChanged(nameof(CanPlayPrevious));
        OnPropertyChanged(nameof(CanPlayNext));
    }

    private bool IsCurrentSession(int sessionVersion)
    {
        return sessionVersion == Volatile.Read(ref _playbackSessionVersion);
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        if (_playbackController.CurrentScope != PlaybackScope.Sentence)
            return;

        if (string.Equals(PlaybackMode, "单句循环", StringComparison.Ordinal))
        {
            _playbackController.Play();
            _setStatusText($"正在循环第 {CurrentSentence?.Index + 1 ?? 1} 句。");
        }
        else if (string.Equals(PlaybackMode, "自动下一句", StringComparison.Ordinal))
        {
            var next = GetRelativeSentence(1);
            if (next is null)
            {
                _playbackController.CurrentScope = PlaybackScope.None;
                _setStatusText("逐句播放完成。");
                AppLogger.Info("Sentence playback completed at final sentence.");
                return;
            }

            _ = PlaySentenceAsync(next, CancellationToken.None, startNewSession: false);
        }
        else
        {
            _playbackController.CurrentScope = PlaybackScope.None;
            _setStatusText("播放完成。");
            AppLogger.Info("Sentence playback completed.");
        }
    }
}
