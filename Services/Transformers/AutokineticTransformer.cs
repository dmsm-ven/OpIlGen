namespace OpIlGen.Services.Transformers;

/// <summary>
/// Приближение к «Autokinetic Illusion» (Sarcone, Smith, Waeber, 2014): изображение строится из параллельных линий,
/// толщина которых плавно меняется вместе с яркостью и сходит на острия (игольчатые окончания) на границах фигур.
/// </summary>
public sealed class AutokineticTransformer : PixelTransformerBase
{
    /// <summary>Сколько линий умещается вдоль меньшей стороны изображения.</summary>
    private const int DefaultLinesAcross = 90;

    /// <summary>Сколько раз размывается карта темноты (чем больше, тем плавнее острия).</summary>
    private const int DefaultBlurPasses = 2;

    /// <summary>Доля самых тёмных и самых светлых пикселей, отсекаемая при растяжении контраста, %.</summary>
    private const int DefaultClipPercent = 2;

    private const double PercentScale = 100.0;

    /// <summary>Минимальный шаг между линиями, px.</summary>
    private const int MinLineSpacing = 6;

    /// <summary>Минимальный диапазон яркости при растяжении (защита от деления на ноль).</summary>
    private const float MinBrightnessRange = 1f;

    /// <summary>Половина пикселя: смещение к центру пикселя и сглаживание краёв линии.</summary>
    private const double HalfPixel = 0.5;

    private static readonly TransformerVariable LinesAcrossVariable = new()
    {
        Name = "Линий по меньшей стороне",
        Key = "lines_across",
        Description = "Сколько параллельных линий умещается вдоль меньшей стороны изображения. " +
                      "Больше линий - тоньше и детальнее узор.",
        MinValue = 30,
        DefaultValue = DefaultLinesAcross,
        MaxValue = 200,
        Step = 1
    };

    private static readonly TransformerVariable BlurPassesVariable = new()
    {
        Name = "Сглаживание острий",
        Key = "blur_passes",
        Description = "Сколько раз размывается карта темноты. Чем больше значение, тем плавнее и длиннее " +
                      "острия на границах фигур.",
        MinValue = 1,
        DefaultValue = DefaultBlurPasses,
        MaxValue = 4,
        Step = 1
    };

    private static readonly TransformerVariable ClipVariable = new()
    {
        Name = "Отсечение яркости, %",
        Key = "clip_percent",
        Description = "Какая доля самых тёмных и самых светлых пикселей игнорируется при растяжении контраста. " +
                      "Чем больше значение, тем контрастнее результат.",
        MinValue = 0,
        DefaultValue = DefaultClipPercent,
        MaxValue = 20,
        Step = 1
    };

    public override string Name => "Автокинетическая иллюзия";
    public override string Key => "autokinetic";
    public override string Description =>
        "Параллельные линии, которые на границах тёмных фигур сужаются до острия. Края фигур могут казаться " +
        "пульсирующими или расползающимися. Лучше работает на контрастных силуэтах; эффект индивидуален.";

    public override TransformerVariable[] AvailableCustomVariables { get; } =
        [LinesAcrossVariable, BlurPassesVariable, ClipVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int linesAcross = (int)Math.Round(customVariables.GetValue(LinesAcrossVariable));
        int blurPasses = (int)Math.Round(customVariables.GetValue(BlurPassesVariable));
        double clip = customVariables.GetValue(ClipVariable) / PercentScale;

        var lum = BitmapHelper.ToLuminance(pixels);
        var darkness = ToStretchedDarkness(lum, clip);

        int spacing = Math.Max(MinLineSpacing, Math.Min(width, height) / linesAcross);

        // Сглаживание темноты по площади: из-за него толщина линий на границах убывает постепенно,
        // и линии заканчиваются остриями, а не обрубками.
        for (int pass = 0; pass < blurPasses; pass++)
        {
            BoxBlur(darkness, width, height, spacing);
        }

        var result = new byte[pixels.Length];
        Array.Fill(result, BitmapHelper.White);

        double halfSpacing = spacing / 2.0;

        for (int y = 0; y < height; y++)
        {
            double center = (y / spacing) * spacing + halfSpacing;
            double dy = Math.Abs(y + HalfPixel - center);
            int sampleY = Math.Min(height - 1, (int)center);

            for (int x = 0; x < width; x++)
            {
                double halfThickness = halfSpacing * darkness[sampleY * width + x];
                double coverage = Math.Clamp(halfThickness - dy + HalfPixel, 0.0, 1.0);

                if (coverage > 0)
                {
                    BitmapHelper.SetGray(result, y * width + x, (byte)(BitmapHelper.White * (1.0 - coverage)));
                }
            }
        }

        return result;
    }

    /// <summary>Темнота 0..1 с растяжением контраста; clip - доля пикселей, отсекаемая с каждого края гистограммы.</summary>
    private static float[] ToStretchedDarkness(float[] lum, double clip)
    {
        float low = BitmapHelper.Percentile(lum, clip);
        float high = BitmapHelper.Percentile(lum, 1.0 - clip);
        if (high - low < MinBrightnessRange) high = low + MinBrightnessRange;

        var darkness = new float[lum.Length];
        for (int i = 0; i < lum.Length; i++)
        {
            darkness[i] = Math.Clamp(1f - (lum[i] - low) / (high - low), 0f, 1f);
        }
        return darkness;
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
