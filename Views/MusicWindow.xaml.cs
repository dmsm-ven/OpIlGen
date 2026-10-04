using OpIlGen.ViewModels;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace OpIlGen.Views;

public partial class MusicWindow : Window
{
    private readonly MusicViewModel _viewModel;

    public MusicWindow(MusicViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Shutdown();

        // Подхватываем последний трек, как только окно показано
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }

    // Пока пользователь тянет ползунок позиции, таймер не должен его двигать
    private void PositionSlider_DragStarted(object sender, DragStartedEventArgs e) => _viewModel.BeginSeek();

    private void PositionSlider_DragCompleted(object sender, DragCompletedEventArgs e) => _viewModel.EndSeek();
}
