using System.IO;
using System.Windows.Media.Imaging;

namespace OpIlGen.Services;

public sealed class ImageService : IImageService
{
    private static string OutputDirectory => Path.Combine(AppContext.BaseDirectory, "Output");

    public BitmapSource Load(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad; // не держим файл открытым
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public string SaveResult(BitmapSource image, string sourcePath, string transformerKey)
    {
        Directory.CreateDirectory(OutputDirectory);

        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var resultPath = Path.Combine(OutputDirectory, $"{name}_{transformerKey}.png");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var stream = File.Create(resultPath);
        encoder.Save(stream);

        return resultPath;
    }
}
