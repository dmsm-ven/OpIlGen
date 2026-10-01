using System.Windows.Media.Imaging;

namespace OpIlGen.Services;

public interface IFullScreenService
{
    /// <summary>Показывает изображение в полноэкранном окне.</summary>
    void Show(BitmapSource image);
}
