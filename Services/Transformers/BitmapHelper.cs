using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpIlGen.Services.Transformers;

internal static class BitmapHelper
{
    public static byte[] ReadBgra32(BitmapSource source, out int width, out int height)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        width = converted.PixelWidth;
        height = converted.PixelHeight;

        int stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    public static BitmapSource CreateBgra32(byte[] pixels, int width, int height, BitmapSource dpiSource)
    {
        var result = BitmapSource.Create(
            width, height, dpiSource.DpiX, dpiSource.DpiY,
            PixelFormats.Bgra32, null, pixels, width * 4);
        result.Freeze();
        return result;
    }

    /// <summary>Смешивает прозрачные пиксели с белым фоном (на месте). После вызова alpha = 255.</summary>
    public static void FlattenOnWhite(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int a = pixels[i + 3];
            if (a == 255) continue;

            pixels[i] = (byte)((pixels[i] * a + 255 * (255 - a)) / 255);
            pixels[i + 1] = (byte)((pixels[i + 1] * a + 255 * (255 - a)) / 255);
            pixels[i + 2] = (byte)((pixels[i + 2] * a + 255 * (255 - a)) / 255);
            pixels[i + 3] = 255;
        }
    }

    /// <summary>Яркость каждого пикселя (0..255). Ожидает, что альфа уже сведена к 255.</summary>
    public static float[] ToLuminance(byte[] pixels)
    {
        var lum = new float[pixels.Length / 4];
        for (int p = 0, i = 0; p < lum.Length; p++, i += 4)
        {
            lum[p] = 0.114f * pixels[i] + 0.587f * pixels[i + 1] + 0.299f * pixels[i + 2];
        }
        return lum;
    }

    public static void SetGray(byte[] pixels, int pixelIndex, byte value)
    {
        int i = pixelIndex * 4;
        pixels[i] = value;
        pixels[i + 1] = value;
        pixels[i + 2] = value;
        pixels[i + 3] = 255;
    }
}
