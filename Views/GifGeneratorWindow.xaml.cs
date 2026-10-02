using OpIlGen.ViewModels;
using System.Windows;

namespace OpIlGen.Views;

public partial class GifGeneratorWindow : Window
{
    public GifGeneratorWindow(GifGeneratorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
