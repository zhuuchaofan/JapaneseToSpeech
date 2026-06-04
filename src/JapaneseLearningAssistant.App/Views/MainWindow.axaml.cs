using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using JapaneseLearningAssistant.App.ViewModels;

namespace JapaneseLearningAssistant.App.Views;

public partial class MainWindow : Window
{
    private const double CompactLayoutThreshold = 1220;
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnSizeChanged;
        UpdateLayoutMode(Bounds.Width);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateLayoutMode(e.NewSize.Width);
    }

    private void UpdateLayoutMode(double width)
    {
        var isCompact = width < CompactLayoutThreshold;
        MainContentGrid.ColumnDefinitions[0].Width = new GridLength(isCompact ? 280 : 310);
        MainContentGrid.ColumnDefinitions[2].Width = new GridLength(isCompact ? 0 : 300);
        MainContentGrid.ColumnSpacing = isCompact ? 8 : 12;

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsCompactLayout = isCompact;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentSentenceAudioItem))
        {
            ScrollCurrentSentenceIntoView();
        }
    }

    private void ScrollCurrentSentenceIntoView()
    {
        if (_viewModel?.CurrentSentenceAudioItem is not { } item)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            SentenceListBox.ScrollIntoView(item);
            CompactSentenceListBox.ScrollIntoView(item);
        }, DispatcherPriority.Background);
    }

    private void SentenceListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.IsBusy)
        {
            return;
        }

        if (viewModel.PlaySelectedSentenceCommand.CanExecute(null))
        {
            viewModel.PlaySelectedSentenceCommand.Execute(null);
        }
    }

    private void HistoryListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.IsBusy)
        {
            return;
        }

        if (viewModel.RestoreHistoryCommand.CanExecute(viewModel.SelectedHistoryItem))
        {
            viewModel.RestoreHistoryCommand.Execute(viewModel.SelectedHistoryItem);
        }
    }
}
