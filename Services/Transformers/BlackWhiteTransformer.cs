using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpIlGen.Services.Transformers;

/// <summary>Строго чёрно-белое изображение (только 2 цвета, без оттенков серого).</summary>
public sealed class BlackWhiteTransformer : IImageTransformer
{
    private const double Threshold = 128;

    public string Name => "Чёрно-белое (2 цвета)";
    public string Key => "black_white";

    public BitmapSource Transform(BitmapSource source)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            double b = pixels[i];
            double g = pixels[i + 1];
            double r = pixels[i + 2];
            double a = pixels[i + 3] / 255.0;

            // Прозрачность смешиваем с белым фоном
            r = r * a + 255 * (1 - a);
            g = g * a + 255 * (1 - a);
            b = b * a + 255 * (1 - a);

            double luminance = 0.299 * r + 0.587 * g + 0.114 * b;
            byte value = luminance >= Threshold ? (byte)255 : (byte)0;

            pixels[i] = value;
            pixels[i + 1] = value;
            pixels[i + 2] = value;
            pixels[i + 3] = 255;
        }

        var result = BitmapSource.Create(
            width, height, source.DpiX, source.DpiY,
            PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }
}
