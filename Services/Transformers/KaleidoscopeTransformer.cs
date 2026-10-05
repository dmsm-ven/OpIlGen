using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

/// <summary>
/// Калейдоскоп: плоскость симметрично «собирается» из одного фрагмента изображения.
/// Типы симметрии: радиальные секторы вокруг центра, зеркало по оси, четыре четверти и плитка.
/// Узор можно поворачивать целиком, а результат плавно смешивать с оригиналом.
/// </summary>
public sealed class KaleidoscopeTransformer : PixelTransformerBase
{
    private const string Id = "kaleidoscope";

    private const double DefaultSymmetry = 0;
    private const double DefaultSegments = 8;
    private const double DefaultPatternRotationDegrees = 0;
    private const double DefaultSourceRotationDegrees = 0;
    private const double DefaultZoomPercent = 100;
    private const double DefaultMirror = 1;
    private const double DefaultBlendPercent = 100;

    private const double DegreesToRadians = Math.PI / 180.0;

    private const int SymmetryRadial = 0;
    private const int SymmetryAxis = 1;
    private const int SymmetryQuadrants = 2;

    private static readonly TransformerVariable SymmetryVariable =
        TransformerVariableFactory.Create(Id, "symmetry", 0, DefaultSymmetry, 3, 1);

    private static readonly TransformerVariable SegmentsVariable =
        TransformerVariableFactory.Create(Id, "segments", 2, DefaultSegments, 24, 1);

    private static readonly TransformerVariable PatternRotationVariable =
        TransformerVariableFactory.Create(Id, "pattern_rotation", 0, DefaultPatternRotationDegrees, 360, 5);

    // Ключ "rotation" остаётся прежним, чтобы сохранённые привязки к звуку не потерялись
    private static readonly TransformerVariable RotationVariable =
        TransformerVariableFactory.Create(Id, "rotation", 0, DefaultSourceRotationDegrees, 360, 5);

    private static readonly TransformerVariable ZoomVariable =
        TransformerVariableFactory.Create(Id, "zoom", 25, DefaultZoomPercent, 300, 5);

    private static readonly TransformerVariable MirrorVariable =
        TransformerVariableFactory.Create(Id, "mirror", 0, DefaultMirror, 1, 1);

    private static readonly TransformerVariable BlendVariable =
        TransformerVariableFactory.Create(Id, "blend", 0, DefaultBlendPercent, 100, 5);

    public KaleidoscopeTransformer(ILocalizationService localizer) : base(localizer)
    {
    }

    public override string Name => Localizer.Get("transformer.kaleidoscope.name");
    public override string Key => Id;
    public override string Description => Localizer.Get("transformer.kaleidoscope.description");

    public override TransformerVariable[] AvailableCustomVariables { get; } =
    [
        SymmetryVariable, SegmentsVariable, PatternRotationVariable, RotationVariable,
        ZoomVariable, MirrorVariable, BlendVariable
    ];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int symmetry = (int)Math.Round(customVariables.GetValue(SymmetryVariable));
        int segments = (int)Math.Round(customVariables.GetValue(SegmentsVariable));
        double patternRotation = customVariables.GetValue(PatternRotationVariable) * DegreesToRadians;
        double sourceRotation = customVariables.GetValue(RotationVariable) * DegreesToRadians;
        double zoom = customVariables.GetValue(ZoomVariable) / 100.0;
        bool mirror = Math.Round(customVariables.GetValue(MirrorVariable)) >= 1;
        float blend = (float)(customVariables.GetValue(BlendVariable) / 100.0);

        double wedge = 2 * Math.PI / segments;
        double centerX = (width - 1) / 2.0;
        double centerY = (height - 1) / 2.0;

        // Поворот узора: координаты пикселя переводятся в систему, повёрнутую вокруг центра
        double cosPattern = Math.Cos(patternRotation);
        double sinPattern = Math.Sin(patternRotation);
        double cosSource = Math.Cos(sourceRotation);
        double sinSource = Math.Sin(sourceRotation);

        // Размер плитки: всё изображение, уменьшенное в segments раз
        double tileWidth = (double)width / segments;
        double tileHeight = (double)height / segments;

        var result = new byte[pixels.Length];

        Parallel.For(0, height, y =>
        {
            double dy = y - centerY;
            for (int x = 0; x < width; x++)
            {
                double dx = x - centerX;
                double u = dx * cosPattern + dy * sinPattern;
                double v = -dx * sinPattern + dy * cosPattern;

                double sourceX, sourceY;
                switch (symmetry)
                {
                    case SymmetryRadial:
                    {
                        double radius = Math.Sqrt(u * u + v * v) / zoom;

                        // Угол внутри своего сектора; у зеркальных секторов вторая половина отражается
                        double angle = Math.Atan2(v, u) % wedge;
                        if (angle < 0)
                        {
                            angle += wedge;
                        }
                        if (mirror && angle > wedge / 2)
                        {
                            angle = wedge - angle;
                        }
                        angle += sourceRotation;

                        sourceX = centerX + radius * Math.Cos(angle);
                        sourceY = centerY + radius * Math.Sin(angle);
                        break;
                    }

                    case SymmetryAxis:
                    case SymmetryQuadrants:
                    {
                        // Отражение через ось (или обе оси) узора
                        double a = Math.Abs(u);
                        double b = symmetry == SymmetryQuadrants ? Math.Abs(v) : v;
                        sourceX = centerX + (a * cosSource - b * sinSource) / zoom;
                        sourceY = centerY + (a * sinSource + b * cosSource) / zoom;
                        break;
                    }

                    default:
                    {
                        // Плитка: в каждой плитке всё изображение (соседние плитки зеркальные или просто повторяются)
                        double fu = Tile(u / tileWidth + 0.5, mirror);
                        double fv = Tile(v / tileHeight + 0.5, mirror);
                        double a = (fu - 0.5) * width;
                        double b = (fv - 0.5) * height;
                        sourceX = centerX + (a * cosSource - b * sinSource) / zoom;
                        sourceY = centerY + (a * sinSource + b * cosSource) / zoom;
                        break;
                    }
                }

                PixelSampler.SampleBilinear(
                    pixels, width, height, sourceX, sourceY, AddressMode.Mirror,
                    out float blue, out float green, out float red);

                int p = y * width + x;
                if (blend < 1f)
                {
                    int o = p * 4;
                    blue = pixels[o] + (blue - pixels[o]) * blend;
                    green = pixels[o + 1] + (green - pixels[o + 1]) * blend;
                    red = pixels[o + 2] + (red - pixels[o + 2]) * blend;
                }

                PixelSampler.SetPixel(result, p, blue, green, red);
            }
        });

        return result;
    }

    /// <summary>Положение внутри плитки (0..1): зеркально отражается через границу или просто повторяется.</summary>
    private static double Tile(double t, bool mirror)
    {
        if (!mirror)
        {
            return t - Math.Floor(t);
        }

        double f = t - 2 * Math.Floor(t / 2);
        return f > 1 ? 2 - f : f;
    }
}
