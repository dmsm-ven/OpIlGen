using OpIlGen.Services.Transformers;
using System.Windows.Media.Imaging;

namespace OpIlGen.Services;

/// <summary>Простые эффекты над готовым изображением.</summary>
public static class BitmapEffects
{
    private const double RoundingOffset = 0.5;

    /// <summary>Затемняет изображение до чёрного: brightness 1 - без изменений, 0 - полностью чёрное.</summary>
    public static BitmapSource Dim(BitmapSource source, double brightness)
    {
        if (brightness >= 1.0)
        {
            return source;
        }

        brightness = Math.Max(0.0, brightness);

        var pixels = BitmapHelper.ReadBgra32(source, out int width, out int height);
        BitmapHelper.FlattenOnWhite(pixels);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(pixels[i] * brightness + RoundingOffset);
            pixels[i + 1] = (byte)(pixels[i + 1] * brightness + RoundingOffset);
            pixels[i + 2] = (byte)(pixels[i + 2] * brightness + RoundingOffset);
        }

        return BitmapHelper.CreateBgra32(pixels, width, height, source);
    }
}
