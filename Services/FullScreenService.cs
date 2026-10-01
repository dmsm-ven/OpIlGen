using OpIlGen.Views;
using System.Windows;
using System.Windows.Media.Imaging;

namespace OpIlGen.Services;

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
