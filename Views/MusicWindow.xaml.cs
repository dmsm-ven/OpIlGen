using OpIlGen.ViewModels;
using System.Windows;

namespace OpIlGen.Views;

public partial class MusicWindow : Window
{
    public MusicWindow(MusicViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Shutdown();
    }
}
