using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

/// <summary>
/// Поле направлений: по изображению строится поле (вдоль контуров, перпендикулярно градиенту яркости,
/// смешанное с плавным «ветром»), и по нему прокладываются тысячи штрихов. Цвет штриха берётся
/// из изображения, а плотность туши растёт в тёмных местах.
/// </summary>
public sealed class FlowFieldTransformer : PixelTransformerBase
{
    private const string Id = "flow_field";

    private const double DefaultLines = 2500;
    private const double DefaultLength = 50;
    private const double DefaultInfluencePercent = 70;
    private const double DefaultRotationDegrees = 0;

    private const double DegreesToRadians = Math.PI / 180.0;
    private const int Seed = 20240608;
    private const double MaxLuminance = 255.0;
    private const int BlurPasses = 2;

    /// <summary>Перепад яркости (на 2 px размытого изображения), при котором контур считается «полным».</summary>
    private const float EdgeFullMagnitude = 6f;

    /// <summary>Шаг штриха и толщина линии считаются от меньшей стороны: картинка выглядит одинаково при любом размере.</summary>
    private const double ReferenceSide = 500.0;

    private const double HalfWidthFactor = 0.55;

    /// <summary>Штрихи слегка затемняются, чтобы были видны на белом фоне.</summary>
    private const double InkColorFactor = 0.75;

    private const double MinInkAlpha = 0.2;
    private const double DarkInkAlpha = 0.55;

    private static readonly TransformerVariable LinesVariable =
        TransformerVariableFactory.Create(Id, "lines", 200, DefaultLines, 8000, 100);

    private static readonly TransformerVariable LengthVariable =
        TransformerVariableFactory.Create(Id, "length", 10, DefaultLength, 200, 5);

    private static readonly TransformerVariable InfluenceVariable =
        TransformerVariableFactory.Create(Id, "influence", 0, DefaultInfluencePercent, 100, 5);

    private static readonly TransformerVariable RotationVariable =
        TransformerVariableFactory.Create(Id, "rotation", 0, DefaultRotationDegrees, 360, 5);

    public FlowFieldTransformer(ILocalizationService localizer) : base(localizer)
    {
    }

    public override string Name => Localizer.Get("transformer.flow_field.name");
    public override string Key => Id;
    public override string Description => Localizer.Get("transformer.flow_field.description");

    public override TransformerVariable[] AvailableCustomVariables { get; } =
        [LinesVariable, LengthVariable, InfluenceVariable, RotationVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int lines = (int)Math.Round(customVariables.GetValue(LinesVariable));
        int length = (int)Math.Round(customVariables.GetValue(LengthVariable));
        double influence = customVariables.GetValue(InfluenceVariable) / 100.0;
        double rotation = customVariables.GetValue(RotationVariable) * DegreesToRadians;

        double side = Math.Min(width, height);
        double stepLength = Math.Max(1.0, side / ReferenceSide);
        double halfWidth = HalfWidthFactor * stepLength;

        var lum = BitmapHelper.ToLuminance(pixels);
        BuildEdgeField(lum, width, height, rotation, side, out float[] edgeX, out float[] edgeY);

        var result = new byte[pixels.Length];
        Array.Fill(result, BitmapHelper.White);

        // Направление поля в точке: смесь «ветра» и контуров, всегда единичный вектор
        void Field(double x, double y, out double fx, out double fy)
        {
            double noiseAngle = NoiseAngle(x, y, side, rotation);
            double nx = Math.Cos(noiseAngle);
            double ny = Math.Sin(noiseAngle);

            int i = Math.Clamp((int)y, 0, height - 1) * width + Math.Clamp((int)x, 0, width - 1);
            double vx = (1 - influence) * nx + influence * edgeX[i];
            double vy = (1 - influence) * ny + influence * edgeY[i];

            double magnitude = Math.Sqrt(vx * vx + vy * vy);
            if (magnitude < 1e-6)
            {
                fx = nx;
                fy = ny;
            }
            else
            {
                fx = vx / magnitude;
                fy = vy / magnitude;
            }
        }

        void Stamp(double x, double y)
        {
            int cx = Math.Clamp((int)x, 0, width - 1);
            int cy = Math.Clamp((int)y, 0, height - 1);
            int center = cy * width + cx;

            double darkness = 1.0 - lum[center] / MaxLuminance;
            double alpha = MinInkAlpha + DarkInkAlpha * darkness;
            int o = center * 4;
            double blue = pixels[o] * InkColorFactor;
            double green = pixels[o + 1] * InkColorFactor;
            double red = pixels[o + 2] * InkColorFactor;

            int reach = (int)Math.Ceiling(halfWidth);
            for (int oy = -reach; oy <= reach; oy++)
            {
                int py = cy + oy;
                if (py < 0 || py >= height) continue;

                for (int ox = -reach; ox <= reach; ox++)
                {
                    int px = cx + ox;
                    if (px < 0 || px >= width) continue;

                    double dx = px + 0.5 - x;
                    double dy = py + 0.5 - y;
                    double coverage = Math.Clamp(halfWidth + 0.5 - Math.Sqrt(dx * dx + dy * dy), 0, 1);
                    if (coverage <= 0) continue;

                    double a = alpha * coverage;
                    int q = (py * width + px) * 4;
                    result[q] = (byte)(result[q] * (1 - a) + blue * a + 0.5);
                    result[q + 1] = (byte)(result[q + 1] * (1 - a) + green * a + 0.5);
                    result[q + 2] = (byte)(result[q + 2] * (1 - a) + red * a + 0.5);
                }
            }
        }

        void Trace(double startX, double startY, double dirX, double dirY, int steps)
        {
            double x = startX, y = startY, dx = dirX, dy = dirY;
            for (int s = 0; s < steps; s++)
            {
                Field(x, y, out double fx, out double fy);

                // Контурное направление знака не имеет: выбираем сторону, продолжающую движение
                if (fx * dx + fy * dy < 0)
                {
                    fx = -fx;
                    fy = -fy;
                }

                dx = fx;
                dy = fy;
                x += dx * stepLength;
                y += dy * stepLength;
                if (x < 0 || y < 0 || x >= width || y >= height) break;

                Stamp(x, y);
            }
        }

        // Старты по сетке с случайным сдвигом: покрытие равномернее, чем при чистом random
        int cols = (int)Math.Ceiling(Math.Sqrt(lines * (double)width / height));
        int rows = (int)Math.Ceiling(lines / (double)cols);
        int halfSteps = Math.Max(1, length / 2);
        var rng = new Random(Seed);

        for (int gy = 0; gy < rows; gy++)
        {
            for (int gx = 0; gx < cols; gx++)
            {
                double startX = (gx + rng.NextDouble()) * width / cols;
                double startY = (gy + rng.NextDouble()) * height / rows;

                Field(startX, startY, out double fx, out double fy);
                Trace(startX, startY, fx, fy, halfSteps);
                Trace(startX, startY, -fx, -fy, halfSteps);
            }
        }

        return result;
    }

    /// <summary>
    /// Единичные векторы вдоль контуров (перпендикулярно градиенту яркости), повёрнутые на угол поля,
    /// умноженные на «силу контура» 0..1: на ровных местах вектор нулевой.
    /// </summary>
    private static void BuildEdgeField(
        float[] lum, int width, int height, double rotation, double side,
        out float[] edgeX, out float[] edgeY)
    {
        var blurred = (float[])lum.Clone();
        int radius = Math.Max(2, (int)(side / 120));
        for (int pass = 0; pass < BlurPasses; pass++)
        {
            BitmapHelper.BoxBlur(blurred, width, height, radius);
        }

        var ex = new float[width * height];
        var ey = new float[width * height];
        double cos = Math.Cos(rotation);
        double sin = Math.Sin(rotation);

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                int i = y * width + x;
                float gx = blurred[i + 1] - blurred[i - 1];
                float gy = blurred[i + width] - blurred[i - width];
                float magnitude = MathF.Sqrt(gx * gx + gy * gy);
                if (magnitude < 1e-3f)
                {
                    continue;
                }

                double weight = Math.Min(1f, magnitude / EdgeFullMagnitude);
                double ux = -gy / magnitude;
                double uy = gx / magnitude;
                ex[i] = (float)((ux * cos - uy * sin) * weight);
                ey[i] = (float)((ux * sin + uy * cos) * weight);
            }
        }

        edgeX = ex;
        edgeY = ey;
    }

    /// <summary>Плавный «ветер»: угол медленно меняется по изображению (сумма синусов разных частот).</summary>
    private static double NoiseAngle(double x, double y, double side, double rotation)
    {
        double nx = x / side;
        double ny = y / side;
        double n = Math.Sin(nx * 5.1 + 1.3)
                   + Math.Sin(ny * 4.3 + 2.1)
                   + Math.Sin((nx + ny) * 3.7 + 0.7)
                   + 0.5 * Math.Sin((nx - ny) * 6.3 + 4.0);
        return n * 1.1 + rotation;
    }
}
