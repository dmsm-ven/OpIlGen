using System.Windows.Media.Imaging;

namespace OpIlGen.Services;

public interface IImageService
{
    BitmapSource Load(string path);

    /// <summary>Сохраняет изображение в папку Output рядом с программой. Возвращает путь к файлу.</summary>
    string SaveResult(BitmapSource image, string sourcePath, string transformerKey);
}
