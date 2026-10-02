namespace OpIlGen.Services.Transformers;

public sealed class HalftoneTransformer : PixelTransformerBase
{
    /// <summary>Минимальный размер ячейки сетки, px.</summary>
    private const int MinCellSize = 6;

    /// <summary>Размер ячейки = меньшая сторона изображения / это значение.</summary>
    private const int CellSizeDivisor = 70;

    /// <summary>Угол наклона сетки точек (45° как в газетном растре).</summary>
    private const double GridAngle = Math.PI / 4;

    private const double CellCenterOffset = 0.5;

    /// <summary>Радиус точки, полностью закрашивающей ячейку, в долях размера ячейки: sqrt(2) / 2.</summary>
    private const double FullCoverageRadiusFactor = 0.7071;

    private const double MaxLuminance = 255.0;

    /// <summary>Значение в кэше для ячейки, яркость которой ещё не считалась.</summary>
    private const float NotComputed = -1f;

    /// <summary>Сетка отсчётов SamplesPerSide x SamplesPerSide для средней яркости ячейки.</summary>
    private const int SamplesPerSide = 5;

    private const double SampleCenterOffset = 0.5;

    public override string Name => "Полутоновый растр (halftone)";
    public override string Key => "halftone";
    public override string Description =>
        "Изображение из чёрных точек разного размера на сетке под углом 45° (как в газетах). " +
        "С расстояния точки сливаются в полутона.";

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        var lum = BitmapHelper.ToLuminance(pixels);
        var result = new byte[pixels.Length];
        Array.Fill(result, BitmapHelper.White);

        double cell = Math.Max(MinCellSize, Math.Min(width, height) / CellSizeDivisor);
        double cos = Math.Cos(GridAngle), sin = Math.Sin(GridAngle);

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
