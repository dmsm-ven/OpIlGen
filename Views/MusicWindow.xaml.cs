using OpIlGen.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

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

    // Enter применяет введённое число сразу, не дожидаясь потери фокуса
    private void FixedValueTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox textBox)
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
    }

    // Пока пользователь тянет ползунок позиции, таймер не должен его двигать
    private void PositionSlider_DragStarted(object sender, DragStartedEventArgs e) => _viewModel.BeginSeek();

    private void PositionSlider_DragCompleted(object sender, DragCompletedEventArgs e) => _viewModel.EndSeek();
}
