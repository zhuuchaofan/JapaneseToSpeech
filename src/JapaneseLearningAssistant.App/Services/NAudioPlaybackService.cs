using NAudio.Wave;

namespace JapaneseLearningAssistant.App.Services;

public sealed class NAudioPlaybackService : IAudioPlaybackService
{
    private AudioFileReader? _reader;
    private WaveOutEvent? _output;
    private bool _isManualStop;

    public string? LoadedFilePath { get; private set; }
    public AudioPlaybackState State { get; private set; } = AudioPlaybackState.Empty;
    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public event EventHandler? PlaybackEnded;
    public event EventHandler? StateChanged;

    public void Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("音频文件不存在。", filePath);
        }

        DisposeCurrent();

        _reader = new AudioFileReader(filePath);
        _output = new WaveOutEvent();
        _output.Init(_reader);
        _output.PlaybackStopped += OnPlaybackStopped;

        LoadedFilePath = filePath;
        SetState(AudioPlaybackState.Stopped);
    }

    public void Play()
    {
        if (_output is null || _reader is null)
        {
            return;
        }

        if (_reader.CurrentTime >= _reader.TotalTime)
        {
            _reader.CurrentTime = TimeSpan.Zero;
        }

        _output.Play();
        SetState(AudioPlaybackState.Playing);
    }

    public void Pause()
    {
        if (_output is null || State != AudioPlaybackState.Playing)
        {
            return;
        }

        _output.Pause();
        SetState(AudioPlaybackState.Paused);
    }

    public void Stop()
    {
        if (_output is null || _reader is null)
        {
            return;
        }

        _isManualStop = true;
        _output.Stop();
        _reader.CurrentTime = TimeSpan.Zero;
        _isManualStop = false;
        SetState(AudioPlaybackState.Stopped);
    }

    public void Seek(TimeSpan position)
    {
        if (_reader is null)
        {
            return;
        }

        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        if (position > _reader.TotalTime)
        {
            position = _reader.TotalTime;
        }

        _reader.CurrentTime = position;
    }

    public void Dispose()
    {
        DisposeCurrent();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_reader is null || _isManualStop)
        {
            return;
        }

        if (State == AudioPlaybackState.Playing)
        {
            _reader.CurrentTime = TimeSpan.Zero;
            SetState(AudioPlaybackState.Stopped);
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DisposeCurrent()
    {
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _isManualStop = true;
            _output.Stop();
            _isManualStop = false;
            _output.Dispose();
        }

        _reader?.Dispose();
        _output = null;
        _reader = null;
        LoadedFilePath = null;
        SetState(AudioPlaybackState.Empty);
    }

    private void SetState(AudioPlaybackState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
