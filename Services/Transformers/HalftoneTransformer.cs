namespace OpIlGen.Services.Transformers;

public sealed class HalftoneTransformer : PixelTransformerBase
{
    public override string Name => "Полутоновый растр (halftone)";
    public override string Key => "halftone";
    public override string Description =>
        "Изображение из чёрных точек разного размера на сетке под углом 45° (как в газетах). " +
        "С расстояния точки сливаются в полутона.";

    protected override byte[] Process(byte[] pixels, int width, int height)
    {
        var lum = BitmapHelper.ToLuminance(pixels);
        var result = new byte[pixels.Length];
        Array.Fill(result, (byte)255);

        double cell = Math.Max(6, Math.Min(width, height) / 70);
        double cos = Math.Cos(Math.PI / 4), sin = Math.Sin(Math.PI / 4);

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
        Array.Fill(cache, -1f);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double u = x * cos + y * sin;
                double v = -x * sin + y * cos;

                int i = (int)Math.Floor(u / cell);
                int j = (int)Math.Floor(v / cell);
                double cu = (i + 0.5) * cell;
                double cv = (j + 0.5) * cell;

                int cacheIdx = (j - jMin) * cols + (i - iMin);
                if (cache[cacheIdx] < 0)
                {
                    double cx = cu * cos - cv * sin;
                    double cy = cu * sin + cv * cos;
                    cache[cacheIdx] = SampleAverage(lum, width, height, cx, cy, cell / 2);
                }

                double darkness = 1.0 - cache[cacheIdx] / 255.0;
                double radius = cell * 0.7071 * Math.Sqrt(Math.Max(0, darkness));

                double du = u - cu, dv = v - cv;
                if (du * du + dv * dv <= radius * radius)
                {
                    BitmapHelper.SetGray(result, y * width + x, 0);
                }
            }
        }

        return result;
    }

    /// <summary>Средняя яркость в квадрате вокруг точки (сетка 5x5 отсчётов).</summary>
    private static float SampleAverage(float[] lum, int w, int h, double cx, double cy, double half)
    {
        const int n = 5;
        double sum = 0;

        for (int sy = 0; sy < n; sy++)
        {
            for (int sx = 0; sx < n; sx++)
            {
                double px = cx - half + (sx + 0.5) * (2 * half / n);
                double py = cy - half + (sy + 0.5) * (2 * half / n);
                int ix = Math.Clamp((int)px, 0, w - 1);
                int iy = Math.Clamp((int)py, 0, h - 1);
                sum += lum[iy * w + ix];
            }
        }

        return (float)(sum / (n * n));
    }
}
