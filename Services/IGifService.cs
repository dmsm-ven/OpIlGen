using OpIlGen.Localization;
using OpIlGen.Services.Transformers;
using System.IO;
using System.Windows.Media.Imaging;

namespace OpIlGen.Services;

public interface IGifService
{
    public string OutputDirectory { get; }
    /// <summary>
    /// Склеивает кадры в зацикленную GIF-анимацию и сохраняет её в папку Output/gif под именем исходного файла.
    /// Кадры перебираются по одному, поэтому их можно передавать лениво (не держать все в памяти).
    /// Возвращает путь к созданному файлу.
    /// </summary>
    string Save(IEnumerable<BitmapSource> frames, string sourcePath, int frameDelayMs);
}

public sealed class GifService : IGifService
{
    public string OutputDirectory => Path.Combine(_imageService.OutputDirectory, GifFolderName);
    private const string GifFolderName = "gif";

    private readonly IImageService _imageService;
    private readonly ILocalizationService _localizer;

    public GifService(IImageService imageService, ILocalizationService localizer)
    {
        _imageService = imageService;
        _localizer = localizer;
    }

    public string Save(IEnumerable<BitmapSource> frames, string sourcePath, int frameDelayMs)
    {
        Directory.CreateDirectory(OutputDirectory);

        var gifPath = Path.Combine(OutputDirectory, Path.GetFileNameWithoutExtension(sourcePath) + ".gif");

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
                    throw new InvalidOperationException(_localizer.Get("gif.error.no_frames"));
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
