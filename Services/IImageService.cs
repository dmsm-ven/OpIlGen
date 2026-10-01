using System.Windows.Media.Imaging;

namespace OpIlGen.Services;

public interface IImageService
{
    /// <summary>Папка Output рядом с программой, куда сохраняются результаты.</summary>
    string OutputDirectory { get; }

    BitmapSource Load(string path);

    /// <summary>Сохраняет изображение в папку Output рядом с программой. Возвращает путь к файлу.</summary>
    string SaveResult(BitmapSource image, string sourcePath, string transformerKey);
}
