using OpIlGen.ViewModels;
using System.Windows;

namespace OpIlGen.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Если результат ещё не успел сохраниться (отложенное сохранение) - сохраняем при закрытии
        Closing += (_, _) => viewModel.FlushPendingSave();
    }
}
