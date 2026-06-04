using Avalonia.Controls;
using Avalonia.Input;
using JapaneseLearningAssistant.App.ViewModels;

namespace JapaneseLearningAssistant.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
