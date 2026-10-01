using OpIlGen.ViewModels;
using System.Windows;

namespace OpIlGen.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
