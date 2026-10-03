using OpIlGen.Localization;

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

    // Значения параметра «направление линий»
    private const int HorizontalLines = 1;
    private const int VerticalLines = 2;
    private const int DefaultLinesDirection = HorizontalLines;

    private const double PercentScale = 100.0;

    /// <summary>Минимальный шаг между линиями, px.</summary>
    private const int MinLineSpacing = 6;

    /// <summary>Минимальный диапазон яркости при растяжении (защита от деления на ноль).</summary>
    private const float MinBrightnessRange = 1f;

    /// <summary>Половина пикселя: смещение к центру пикселя и сглаживание краёв линии.</summary>
    private const double HalfPixel = 0.5;

    private static readonly TransformerVariable LinesAcrossVariable = new()
    {
        NameKey = "transformer.autokinetic.var.lines_across.name",
        Key = "lines_across",
        DescriptionKey = "transformer.autokinetic.var.lines_across.description",
        MinValue = 30,
        DefaultValue = DefaultLinesAcross,
        MaxValue = 200,
        Step = 1
    };

    private static readonly TransformerVariable DirectionVariable = new()
    {
        NameKey = "transformer.autokinetic.var.lines_direction.name",
        Key = "lines_direction",
        DescriptionKey = "transformer.autokinetic.var.lines_direction.description",
        MinValue = HorizontalLines,
        DefaultValue = DefaultLinesDirection,
        MaxValue = VerticalLines,
        Step = 1
    };

    private static readonly TransformerVariable BlurPassesVariable = new()
    {
        NameKey = "transformer.autokinetic.var.blur_passes.name",
        Key = "blur_passes",
        DescriptionKey = "transformer.autokinetic.var.blur_passes.description",
        MinValue = 1,
        DefaultValue = DefaultBlurPasses,
        MaxValue = 4,
        Step = 1
    };

    private static readonly TransformerVariable ClipVariable = new()
    {
        NameKey = "transformer.autokinetic.var.clip_percent.name",
        Key = "clip_percent",
        DescriptionKey = "transformer.autokinetic.var.clip_percent.description",
        MinValue = 0,
        DefaultValue = DefaultClipPercent,
        MaxValue = 20,
        Step = 1
    };

    public AutokineticTransformer(ILocalizationService localizer) : base(localizer)
    {
    }

    public override string Name => Localizer.Get("transformer.autokinetic.name");
    public override string Key => "autokinetic";
    public override string Description => Localizer.Get("transformer.autokinetic.description");

    public override TransformerVariable[] AvailableCustomVariables { get; } =
        [LinesAcrossVariable, DirectionVariable, BlurPassesVariable, ClipVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int linesAcross = (int)Math.Round(customVariables.GetValue(LinesAcrossVariable));
        int direction = (int)Math.Round(customVariables.GetValue(DirectionVariable));
        int blurPasses = (int)Math.Round(customVariables.GetValue(BlurPassesVariable));
        double clip = customVariables.GetValue(ClipVariable) / PercentScale;

        var lum = BitmapHelper.ToLuminance(pixels);
        var darkness = ToStretchedDarkness(lum, clip);

        int spacing = Math.Max(MinLineSpacing, Math.Min(width, height) / linesAcross);

        // Сглаживание темноты по площади: из-за него толщина линий на границах убывает постепенно,
        // и линии заканчиваются остриями, а не обрубками.
        for (int pass = 0; pass < blurPasses; pass++)
        {
            BitmapHelper.BoxBlur(darkness, width, height, spacing);
        }

        var result = new byte[pixels.Length];
        Array.Fill(result, BitmapHelper.White);

        double halfSpacing = spacing / 2.0;
        bool vertical = direction == VerticalLines;

        // Для каждой позиции поперёк линий: расстояние до оси ближайшей линии
        // и координата на этой оси, в которой берётся толщина линии
        int acrossLength = vertical ? width : height;
        var distanceToAxis = new double[acrossLength];
        var axisPosition = new int[acrossLength];
        for (int a = 0; a < acrossLength; a++)
        {
            double axis = (a / spacing) * spacing + halfSpacing;
            distanceToAxis[a] = Math.Abs(a + HalfPixel - axis);
            axisPosition[a] = Math.Min(acrossLength - 1, (int)axis);
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Горизонтальные линии: толщина зависит от x на оси линии (строка axisPosition[y]).
                // Вертикальные: толщина зависит от y на оси линии (столбец axisPosition[x]).
                int across = vertical ? x : y;
                int sampleX = vertical ? axisPosition[x] : x;
                int sampleY = vertical ? y : axisPosition[y];

                double halfThickness = halfSpacing * darkness[sampleY * width + sampleX];
                double coverage = Math.Clamp(halfThickness - distanceToAxis[across] + HalfPixel, 0.0, 1.0);

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
}
