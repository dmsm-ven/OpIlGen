namespace OpIlGen.Services.Transformers;

/// <summary>Как обрабатывать координаты за границами изображения.</summary>
internal enum AddressMode
{
    /// <summary>Крайний пиксель повторяется.</summary>
    Clamp,

    /// <summary>Изображение зеркально отражается от края.</summary>
    Mirror,

    /// <summary>Изображение повторяется по кругу.</summary>
    Wrap
}

/// <summary>Выборка пикселей Bgra32 с интерполяцией и работа с цветовой моделью HSV.</summary>
internal static class PixelSampler
{
    private const float MaxChannel = 255f;
    private const float HueSector = 60f;
    private const float FullCircle = 360f;
    private const float Epsilon = 1e-3f;

    /// <summary>Билинейная выборка цвета в точке (x, y). Центр левого верхнего пикселя - (0, 0).</summary>
    public static void SampleBilinear(
        byte[] pixels, int width, int height, double x, double y, AddressMode mode,
        out float b, out float g, out float r)
    {
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        float fx = (float)(x - x0);
        float fy = (float)(y - y0);

        int xa = Fix(x0, width, mode);
        int xb = Fix(x0 + 1, width, mode);
        int ya = Fix(y0, height, mode);
        int yb = Fix(y0 + 1, height, mode);

        int i00 = (ya * width + xa) * 4;
        int i10 = (ya * width + xb) * 4;
        int i01 = (yb * width + xa) * 4;
        int i11 = (yb * width + xb) * 4;

        float w00 = (1 - fx) * (1 - fy);
        float w10 = fx * (1 - fy);
        float w01 = (1 - fx) * fy;
        float w11 = fx * fy;

        b = pixels[i00] * w00 + pixels[i10] * w10 + pixels[i01] * w01 + pixels[i11] * w11;
        g = pixels[i00 + 1] * w00 + pixels[i10 + 1] * w10 + pixels[i01 + 1] * w01 + pixels[i11 + 1] * w11;
        r = pixels[i00 + 2] * w00 + pixels[i10 + 2] * w10 + pixels[i01 + 2] * w01 + pixels[i11 + 2] * w11;
    }

    /// <summary>Записывает непрозрачный пиксель (каналы ограничиваются 0..255).</summary>
    public static void SetPixel(byte[] pixels, int pixelIndex, float b, float g, float r)
    {
        int i = pixelIndex * 4;
        pixels[i] = ToByte(b);
        pixels[i + 1] = ToByte(g);
        pixels[i + 2] = ToByte(r);
        pixels[i + 3] = BitmapHelper.Opaque;
    }

    /// <summary>RGB (0..255) в оттенок (0..360), насыщенность (0..1) и яркость (0..1).</summary>
    public static void RgbToHsv(float r, float g, float b, out float hue, out float saturation, out float value)
    {
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float delta = max - min;

        value = max / MaxChannel;
        saturation = max <= Epsilon ? 0 : delta / max;

        if (delta < Epsilon)
        {
            hue = 0;
            return;
        }

        if (max == r)
        {
            hue = HueSector * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            hue = HueSector * (((b - r) / delta) + 2);
        }
        else
        {
            hue = HueSector * (((r - g) / delta) + 4);
        }

        if (hue < 0)
        {
            hue += FullCircle;
        }
    }

    private static byte ToByte(float value) => (byte)Math.Clamp(value + 0.5f, 0f, MaxChannel);

    private static int Fix(int index, int size, AddressMode mode)
    {
        switch (mode)
        {
            case AddressMode.Wrap:
                index %= size;
                return index < 0 ? index + size : index;

            case AddressMode.Mirror:
                int period = size * 2;
                index %= period;
                if (index < 0)
                {
                    index += period;
                }
                return index < size ? index : period - 1 - index;

            default:
                return index < 0 ? 0 : index >= size ? size - 1 : index;
        }
    }
}
