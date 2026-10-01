using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace OpIlGen.Views;

public partial class FullScreenWindow : Window
{
    public FullScreenWindow(BitmapSource image)
    {
        InitializeComponent();
        Preview.Source = image;
        ResolutionText.Text = $"{image.PixelWidth}x{image.PixelHeight}";
    }

    // Esc или клик мышью - закрыть
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Close();
}
