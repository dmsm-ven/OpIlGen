using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

public sealed class HalftoneTransformer : PixelTransformerBase
{
    /// <summary>Сколько точек растра умещается вдоль меньшей стороны изображения.</summary>
    private const int DefaultDotsAcross = 70;

    /// <summary>Угол наклона сетки точек, градусы (45° как в газетном растре).</summary>
    private const int DefaultGridAngleDegrees = 45;

    /// <summary>Минимальный размер ячейки сетки, px.</summary>
    private const int MinCellSize = 6;

    private const double DegreesToRadians = Math.PI / 180.0;

    private const double CellCenterOffset = 0.5;

    /// <summary>Радиус точки, полностью закрашивающей ячейку, в долях размера ячейки: sqrt(2) / 2.</summary>
    private const double FullCoverageRadiusFactor = 0.7071;

    private const double MaxLuminance = 255.0;

    /// <summary>Значение в кэше для ячейки, яркость которой ещё не считалась.</summary>
    private const float NotComputed = -1f;

    /// <summary>Сетка отсчётов SamplesPerSide x SamplesPerSide для средней яркости ячейки.</summary>
    private const int SamplesPerSide = 5;

    private const double SampleCenterOffset = 0.5;

    private static readonly TransformerVariable DotsAcrossVariable = new()
    {
        NameKey = "transformer.halftone.var.dots_across.name",
        Key = "dots_across",
        DescriptionKey = "transformer.halftone.var.dots_across.description",
        MinValue = 20,
        DefaultValue = DefaultDotsAcross,
        MaxValue = 200,
        Step = 1
    };

    private static readonly TransformerVariable GridAngleVariable = new()
    {
        NameKey = "transformer.halftone.var.grid_angle.name",
        Key = "grid_angle",
        DescriptionKey = "transformer.halftone.var.grid_angle.description",
        MinValue = 0,
        DefaultValue = DefaultGridAngleDegrees,
        MaxValue = 90,
        Step = 5
    };

    public HalftoneTransformer(ILocalizationService localizer) : base(localizer)
    {
    }

    public override string Name => Localizer.Get("transformer.halftone.name");
    public override string Key => "halftone";
    public override string Description => Localizer.Get("transformer.halftone.description");

    public override TransformerVariable[] AvailableCustomVariables { get; } = [DotsAcrossVariable, GridAngleVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int dotsAcross = (int)Math.Round(customVariables.GetValue(DotsAcrossVariable));
        double angle = customVariables.GetValue(GridAngleVariable) * DegreesToRadians;

        var lum = BitmapHelper.ToLuminance(pixels);
        var result = new byte[pixels.Length];
        Array.Fill(result, BitmapHelper.White);

        double cell = Math.Max(MinCellSize, Math.Min(width, height) / dotsAcross);
        double cos = Math.Cos(angle), sin = Math.Sin(angle);

        // Границы изображения в повёрнутой системе координат (u, v)
        double[] us = { 0, width * cos, height * sin, width * cos + height * sin };
        double[] vs = { 0, -width * sin, height * cos, -width * sin + height * cos };

        int iMin = (int)Math.Floor(us.Min() / cell);
        int iMax = (int)Math.Floor(us.Max() / cell);
        int jMin = (int)Math.Floor(vs.Min() / cell);
        int jMax = (int)Math.Floor(vs.Max() / cell);

        int cols = iMax - iMin + 1;
        int rows = jMax - jMin + 1;
        var cache = new float[cols * rows];
        Array.Fill(cache, NotComputed);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double u = x * cos + y * sin;
                double v = -x * sin + y * cos;

                int i = (int)Math.Floor(u / cell);
                int j = (int)Math.Floor(v / cell);
                double cu = (i + CellCenterOffset) * cell;
                double cv = (j + CellCenterOffset) * cell;

                int cacheIdx = (j - jMin) * cols + (i - iMin);
                if (cache[cacheIdx] == NotComputed)
                {
                    double cx = cu * cos - cv * sin;
                    double cy = cu * sin + cv * cos;
                    cache[cacheIdx] = SampleAverage(lum, width, height, cx, cy, cell / 2);
                }

                double darkness = 1.0 - cache[cacheIdx] / MaxLuminance;
                double radius = cell * FullCoverageRadiusFactor * Math.Sqrt(Math.Max(0, darkness));

                double du = u - cu, dv = v - cv;
                if (du * du + dv * dv <= radius * radius)
                {
                    BitmapHelper.SetGray(result, y * width + x, BitmapHelper.Black);
                }
            }
        }

        return result;
    }

    /// <summary>Средняя яркость в квадрате вокруг точки.</summary>
    private static float SampleAverage(float[] lum, int w, int h, double cx, double cy, double half)
    {
        double sum = 0;

        for (int sy = 0; sy < SamplesPerSide; sy++)
        {
            for (int sx = 0; sx < SamplesPerSide; sx++)
            {
                double px = cx - half + (sx + SampleCenterOffset) * (2 * half / SamplesPerSide);
                double py = cy - half + (sy + SampleCenterOffset) * (2 * half / SamplesPerSide);
                int ix = Math.Clamp((int)px, 0, w - 1);
                int iy = Math.Clamp((int)py, 0, h - 1);
                sum += lum[iy * w + ix];
            }
        }

        return (float)(sum / (SamplesPerSide * SamplesPerSide));
    }
}
