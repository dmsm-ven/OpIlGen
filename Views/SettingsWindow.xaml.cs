using OpIlGen.ViewModels;
using System.Windows;

namespace OpIlGen.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
