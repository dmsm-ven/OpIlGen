using System.Windows.Media.Imaging;

namespace OpIlGen.Services;

public interface IFullScreenService
{
    /// <summary>Показывает изображение в окне на весь экран.</summary>
    void Show(BitmapSource image);
}
