using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JapaneseLearningAssistant.App.ViewModels;

public sealed partial class SentenceAudioItemViewModel : ObservableObject
{
    public SentenceAudioItemViewModel(int index, string text, Func<SentenceAudioItemViewModel, Task> playAsync)
    {
        Index = index;
        Text = text;
        PlayCommand = new AsyncRelayCommand(() => playAsync(this));
    }

    public int Index { get; }
    public string Text { get; }
    public IAsyncRelayCommand PlayCommand { get; }
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
