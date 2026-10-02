using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpIlGen.Services.Transformers;

/// <summary>Строго чёрно-белое изображение (только 2 цвета, без оттенков серого).</summary>
public sealed class BlackWhiteTransformer : IImageTransformer
{
    /// <summary>Яркость, начиная с которой пиксель становится белым.</summary>
    private const double Threshold = 128;

    /// <summary>Максимальное значение канала; для альфы означает полную непрозрачность.</summary>
    private const double MaxChannel = 255;

    public string Name => "Чёрно-белое (2 цвета)";
    public string Key => "black_white";
    public string Description => "Жёсткий порог яркости: каждый пиксель становится либо чёрным, либо белым.";

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
            double a = pixels[i + 3] / MaxChannel;

            // Прозрачность смешиваем с белым фоном
            r = r * a + MaxChannel * (1 - a);
            g = g * a + MaxChannel * (1 - a);
            b = b * a + MaxChannel * (1 - a);

            double luminance = BitmapHelper.RedWeight * r
                             + BitmapHelper.GreenWeight * g
                             + BitmapHelper.BlueWeight * b;
            byte value = luminance >= Threshold ? BitmapHelper.White : BitmapHelper.Black;

            pixels[i] = value;
            pixels[i + 1] = value;
            pixels[i + 2] = value;
            pixels[i + 3] = BitmapHelper.Opaque;
        }

        var result = BitmapSource.Create(
            width, height, source.DpiX, source.DpiY,
            PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }
}
