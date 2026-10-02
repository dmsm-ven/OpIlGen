using System.Windows;
using System.Windows.Media.Imaging;
using OpIlGen.Views;

namespace OpIlGen.Services;

public interface IFullScreenService
{
    /// <summary>Показывает изображение в окне на весь экран.</summary>
    void Show(BitmapSource image);
}

public sealed class FullScreenService : IFullScreenService
{
    public void Show(BitmapSource image)
    {
        var window = new FullScreenWindow(image)
        {
            Owner = Application.Current.MainWindow
        };
        window.Show();
    }
}
