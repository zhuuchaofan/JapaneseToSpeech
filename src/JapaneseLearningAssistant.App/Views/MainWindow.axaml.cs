using Avalonia.Controls;
using Avalonia.Input;
using JapaneseLearningAssistant.App.ViewModels;

namespace JapaneseLearningAssistant.App.Views;

public partial class MainWindow : Window
{
    private const double CompactLayoutThreshold = 1220;

    public MainWindow()
    {
        InitializeComponent();
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
}
