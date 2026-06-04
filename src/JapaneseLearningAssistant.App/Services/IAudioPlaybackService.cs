namespace JapaneseLearningAssistant.App.Services;

public enum AudioPlaybackState
{
    Empty,
    Stopped,
    Playing,
    Paused
}

public interface IAudioPlaybackService : IDisposable
{
    string? LoadedFilePath { get; }
    AudioPlaybackState State { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }

    event EventHandler? PlaybackEnded;
    event EventHandler? StateChanged;

    void Load(string filePath);
    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);
}
