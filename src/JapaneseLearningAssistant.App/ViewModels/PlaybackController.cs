using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using JapaneseLearningAssistant.App.Services;

namespace JapaneseLearningAssistant.App.ViewModels;

public enum PlaybackScope
{
    None,
    Whole,
    Sentence
}

public sealed partial class PlaybackController : ObservableObject, IDisposable
{
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly DispatcherTimer _playbackTimer;
    private bool _isUpdatingPosition;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _hasLoadedAudio;

    [ObservableProperty]
    private double _positionSeconds;

    [ObservableProperty]
    private double _durationSeconds;

    [ObservableProperty]
    private string _timeText = "00:00 / 00:00";

    [ObservableProperty]
    private PlaybackScope _currentScope = PlaybackScope.None;

    public event EventHandler? PlaybackEnded;

    public PlaybackController(IAudioPlaybackService audioPlaybackService)
    {
        _audioPlaybackService = audioPlaybackService;
        _audioPlaybackService.StateChanged += OnStateChanged;
        _audioPlaybackService.PlaybackEnded += OnPlaybackEnded;

        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _playbackTimer.Tick += (_, _) => UpdateProgress();
        _playbackTimer.Start();
    }

    public string LoadedFilePath => _audioPlaybackService.LoadedFilePath ?? string.Empty;

    public void Load(string filePath, PlaybackScope scope)
    {
        if (!string.Equals(_audioPlaybackService.LoadedFilePath, filePath, StringComparison.Ordinal))
        {
            _audioPlaybackService.Load(filePath);
        }
        CurrentScope = scope;
        UpdateProgress();
        UpdateStateProperties();
    }

    public void Play()
    {
        _audioPlaybackService.Play();
        UpdateStateProperties();
    }

    public void Pause()
    {
        _audioPlaybackService.Pause();
        UpdateStateProperties();
    }

    public void Toggle()
    {
        if (_audioPlaybackService.State == AudioPlaybackState.Playing)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void Stop()
    {
        if (_audioPlaybackService.State is AudioPlaybackState.Playing or AudioPlaybackState.Paused)
        {
            _audioPlaybackService.Stop();
        }
        CurrentScope = PlaybackScope.None;
        UpdateProgress();
        UpdateStateProperties();
    }

    public void Seek(double seconds)
    {
        if (_isUpdatingPosition) return;
        _audioPlaybackService.Seek(TimeSpan.FromSeconds(seconds));
        UpdateProgress();
    }

    partial void OnPositionSecondsChanged(double value)
    {
        if (_isUpdatingPosition) return;
        _audioPlaybackService.Seek(TimeSpan.FromSeconds(value));
    }

    private void UpdateProgress()
    {
        _isUpdatingPosition = true;
        
        var duration = _audioPlaybackService.Duration;
        var position = _audioPlaybackService.Position;

        DurationSeconds = duration.TotalSeconds;
        PositionSeconds = Math.Min(position.TotalSeconds, Math.Max(duration.TotalSeconds, 0));
        TimeText = $"{FormatDuration(position)} / {FormatDuration(duration)}";
        
        _isUpdatingPosition = false;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"mm\:ss");
    }

    private void UpdateStateProperties()
    {
        IsPlaying = _audioPlaybackService.State == AudioPlaybackState.Playing;
        IsPaused = _audioPlaybackService.State == AudioPlaybackState.Paused;
        HasLoadedAudio = _audioPlaybackService.State != AudioPlaybackState.Empty;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateStateProperties);
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateProgress();
            UpdateStateProperties();
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        });
    }

    public void Dispose()
    {
        _playbackTimer.Stop();
        _audioPlaybackService.StateChanged -= OnStateChanged;
        _audioPlaybackService.PlaybackEnded -= OnPlaybackEnded;
    }
}
