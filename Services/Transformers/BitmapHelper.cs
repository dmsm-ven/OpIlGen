using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpIlGen.Services.Transformers;

/// <summary>
/// Общие операции над пикселями. Формат пикселей везде Bgra32:
/// 4 байта на пиксель в порядке B, G, R, A.
/// </summary>
internal static class BitmapHelper
{
    // Коэффициенты яркости (Rec. 601)
    public const float RedWeight = 0.299f;
    public const float GreenWeight = 0.587f;
    public const float BlueWeight = 0.114f;

    // Значения каналов
    public const byte Black = 0;
    public const byte White = 255;
    public const byte Opaque = 255;

    private const int MaxChannelValue = 255;
    private const int HistogramBins = 256;

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
            if (a == MaxChannelValue) continue;

            pixels[i] = (byte)((pixels[i] * a + MaxChannelValue * (MaxChannelValue - a)) / MaxChannelValue);
            pixels[i + 1] = (byte)((pixels[i + 1] * a + MaxChannelValue * (MaxChannelValue - a)) / MaxChannelValue);
            pixels[i + 2] = (byte)((pixels[i + 2] * a + MaxChannelValue * (MaxChannelValue - a)) / MaxChannelValue);
            pixels[i + 3] = Opaque;
        }
    }

    /// <summary>Яркость каждого пикселя (0..255). Ожидает, что альфа уже сведена к 255.</summary>
    public static float[] ToLuminance(byte[] pixels)
    {
        var lum = new float[pixels.Length / 4];
        for (int p = 0, i = 0; p < lum.Length; p++, i += 4)
        {
            lum[p] = BlueWeight * pixels[i] + GreenWeight * pixels[i + 1] + RedWeight * pixels[i + 2];
        }
        return lum;
    }

    /// <summary>Значение яркости (0..255), ниже которого лежит доля p (0..1) пикселей.</summary>
    public static float Percentile(float[] values, double p)
    {
        var histogram = new int[HistogramBins];
        foreach (var v in values)
        {
            histogram[Math.Clamp((int)v, 0, HistogramBins - 1)]++;
        }

        long target = (long)(values.Length * p);
        long accumulated = 0;
        for (int i = 0; i < histogram.Length; i++)
        {
            accumulated += histogram[i];
            if (accumulated >= target) return i;
        }
        return HistogramBins - 1;
    }

    /// <summary>Разделимое размытие «прямоугольником» (скользящая сумма, края повторяются).</summary>
    public static void BoxBlur(float[] data, int w, int h, int radius)
    {
        var temp = new float[data.Length];
        float size = radius * 2 + 1;

        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            float sum = 0;
            for (int k = -radius; k <= radius; k++)
                sum += data[row + Math.Clamp(k, 0, w - 1)];

            for (int x = 0; x < w; x++)
            {
                temp[row + x] = sum / size;
                sum += data[row + Math.Min(w - 1, x + radius + 1)] - data[row + Math.Max(0, x - radius)];
            }
        }

        for (int x = 0; x < w; x++)
        {
            float sum = 0;
            for (int k = -radius; k <= radius; k++)
                sum += temp[Math.Clamp(k, 0, h - 1) * w + x];

            for (int y = 0; y < h; y++)
            {
                data[y * w + x] = sum / size;
                sum += temp[Math.Min(h - 1, y + radius + 1) * w + x] - temp[Math.Max(0, y - radius) * w + x];
            }
        }
    }

    public static void SetGray(byte[] pixels, int pixelIndex, byte value)
    {
        int i = pixelIndex * 4;
        pixels[i] = value;
        pixels[i + 1] = value;
        pixels[i + 2] = value;
        pixels[i + 3] = Opaque;
    }
}
