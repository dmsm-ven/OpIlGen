using OpIlGen.Services.Transformers;
using System.IO;
using System.Windows.Media.Imaging;

namespace OpIlGen.Services;

public interface IGifService
{
    /// <summary>
    /// Склеивает кадры в зацикленную GIF-анимацию и сохраняет её в папку Output/gif под именем исходного файла.
    /// Кадры перебираются по одному, поэтому их можно передавать лениво (не держать все в памяти).
    /// Возвращает путь к созданному файлу.
    /// </summary>
    string Save(IEnumerable<BitmapSource> frames, string sourcePath, int frameDelayMs);
}

public sealed class GifService : IGifService
{
    private const string GifFolderName = "gif";

    private readonly IImageService _imageService;

    public GifService(IImageService imageService)
    {
        _imageService = imageService;
    }

    public string Save(IEnumerable<BitmapSource> frames, string sourcePath, int frameDelayMs)
    {
        var folder = Path.Combine(_imageService.OutputDirectory, GifFolderName);
        Directory.CreateDirectory(folder);

        var gifPath = Path.Combine(folder, Path.GetFileNameWithoutExtension(sourcePath) + ".gif");

        try
        {
            using (var stream = File.Create(gifPath))
            {
                GifEncoder? encoder = null;

                foreach (var frame in frames)
                {
                    var pixels = BitmapHelper.ReadBgra32(frame, out int width, out int height);
                    BitmapHelper.FlattenOnWhite(pixels);

                    encoder ??= new GifEncoder(stream, width, height);
                    encoder.AddFrame(pixels, frameDelayMs);
                }

                if (encoder is null)
                {
                    throw new InvalidOperationException("Нет кадров для создания GIF.");
                }

                encoder.Finish();
            }
        }
        catch
        {
            // Не оставляем недописанный файл
            if (File.Exists(gifPath))
            {
                File.Delete(gifPath);
            }
            throw;
        }

        return gifPath;
    }
}
