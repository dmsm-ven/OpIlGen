using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace OpIlGen.Views;

public partial class FullScreenWindow : Window
{
    public FullScreenWindow(BitmapSource image)
    {
        InitializeComponent();
        SetImage(image);
    }

    /// <summary>Подменяет изображение (для «живого» режима: картинка меняется, пока окно открыто).</summary>
    public void SetImage(BitmapSource image)
    {
        Picture.Source = image;
        ResolutionText.Text = $"{image.PixelWidth}×{image.PixelHeight}";
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Close();
}
