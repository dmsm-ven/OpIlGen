namespace OpIlGen.Services.Transformers;

/// <summary>
/// Приближение к «Autokinetic Illusion» (Sarcone, Smith, Waeber, 2014): изображение строится из параллельных линий,
/// толщина которых плавно меняется вместе с яркостью и сходит на острия (игольчатые окончания) на границах фигур.
/// </summary>
public sealed class AutokineticTransformer : PixelTransformerBase
{
    public override string Name => "Автокинетическая иллюзия";
    public override string Key => "autokinetic";
    public override string Description =>
        "Параллельные линии, которые на границах тёмных фигур сужаются до острия. Края фигур могут казаться " +
        "пульсирующими или расползающимися. Лучше работает на контрастных силуэтах; эффект индивидуален.";

    protected override byte[] Process(byte[] pixels, int width, int height)
    {
        var lum = BitmapHelper.ToLuminance(pixels);
        var darkness = ToStretchedDarkness(lum);

        int spacing = Math.Max(6, Math.Min(width, height) / 90);

        // Сглаживание темноты по площади: из-за него толщина линий на границах убывает постепенно,
        // и линии заканчиваются остриями, а не обрубками.
        BoxBlur(darkness, width, height, spacing);
        BoxBlur(darkness, width, height, spacing);

        var result = new byte[pixels.Length];
        Array.Fill(result, (byte)255);

        double maxHalfThickness = spacing / 2.0;

        for (int y = 0; y < height; y++)
        {
            double center = (y / spacing) * spacing + spacing / 2.0;
            double dy = Math.Abs(y + 0.5 - center);
            int sampleY = Math.Min(height - 1, (int)center);

            for (int x = 0; x < width; x++)
            {
                double half = maxHalfThickness * darkness[sampleY * width + x];
                double coverage = Math.Clamp(half - dy + 0.5, 0.0, 1.0);

                if (coverage > 0)
                {
                    BitmapHelper.SetGray(result, y * width + x, (byte)(255 * (1.0 - coverage)));
                }
            }
        }

        return result;
    }

    /// <summary>Темнота 0..1 с растяжением контраста по 2-му и 98-му перцентилям.</summary>
    private static float[] ToStretchedDarkness(float[] lum)
    {
        var histogram = new int[256];
        foreach (var l in lum)
        {
            histogram[Math.Clamp((int)l, 0, 255)]++;
        }

        int low = Percentile(histogram, lum.Length, 0.02);
        int high = Percentile(histogram, lum.Length, 0.98);
        if (high - low < 1) high = low + 1;

        var darkness = new float[lum.Length];
        for (int i = 0; i < lum.Length; i++)
        {
            darkness[i] = Math.Clamp(1f - (lum[i] - low) / (high - low), 0f, 1f);
        }
        return darkness;
    }

    private static int Percentile(int[] histogram, int total, double p)
    {
        long target = (long)(total * p);
        long accumulated = 0;
        for (int v = 0; v < histogram.Length; v++)
        {
            accumulated += histogram[v];
            if (accumulated >= target) return v;
        }
        return 255;
    }

    /// <summary>Разделимое размытие «прямоугольником» (скользящая сумма, края повторяются).</summary>
    private static void BoxBlur(float[] data, int w, int h, int radius)
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
}
