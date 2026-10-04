using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

/// <summary>
/// Калейдоскоп: плоскость делится на секторы вокруг центра, и во всех секторах повторяется один и тот же
/// фрагмент изображения (с зеркальным отражением соседних секторов или без него).
/// </summary>
public sealed class KaleidoscopeTransformer : PixelTransformerBase
{
    private const string Id = "kaleidoscope";

    private const double DefaultSegments = 8;
    private const double DefaultRotationDegrees = 0;
    private const double DefaultZoomPercent = 100;
    private const double DefaultMirror = 1;

    private const double DegreesToRadians = Math.PI / 180.0;

    private static readonly TransformerVariable SegmentsVariable =
        TransformerVariableFactory.Create(Id, "segments", 2, DefaultSegments, 24, 1);

    private static readonly TransformerVariable RotationVariable =
        TransformerVariableFactory.Create(Id, "rotation", 0, DefaultRotationDegrees, 360, 5);

    private static readonly TransformerVariable ZoomVariable =
        TransformerVariableFactory.Create(Id, "zoom", 25, DefaultZoomPercent, 300, 5);

    private static readonly TransformerVariable MirrorVariable =
        TransformerVariableFactory.Create(Id, "mirror", 0, DefaultMirror, 1, 1);

    public KaleidoscopeTransformer(ILocalizationService localizer) : base(localizer)
    {
    }

    public override string Name => Localizer.Get("transformer.kaleidoscope.name");
    public override string Key => Id;
    public override string Description => Localizer.Get("transformer.kaleidoscope.description");

    public override TransformerVariable[] AvailableCustomVariables { get; } =
        [SegmentsVariable, RotationVariable, ZoomVariable, MirrorVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int segments = (int)Math.Round(customVariables.GetValue(SegmentsVariable));
        double rotation = customVariables.GetValue(RotationVariable) * DegreesToRadians;
        double zoom = customVariables.GetValue(ZoomVariable) / 100.0;
        bool mirror = Math.Round(customVariables.GetValue(MirrorVariable)) >= 1;

        double wedge = 2 * Math.PI / segments;
        double centerX = (width - 1) / 2.0;
        double centerY = (height - 1) / 2.0;
        var result = new byte[pixels.Length];

        Parallel.For(0, height, y =>
        {
            double dy = y - centerY;
            for (int x = 0; x < width; x++)
            {
                double dx = x - centerX;
                double radius = Math.Sqrt(dx * dx + dy * dy) / zoom;

                // Угол внутри своего сектора; у зеркальных секторов вторая половина отражается
                double angle = Math.Atan2(dy, dx) % wedge;
                if (angle < 0)
                {
                    angle += wedge;
                }
                if (mirror && angle > wedge / 2)
                {
                    angle = wedge - angle;
                }
                angle += rotation;

                PixelSampler.SampleBilinear(
                    pixels, width, height,
                    centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle),
                    AddressMode.Mirror, out float b, out float g, out float r);
                PixelSampler.SetPixel(result, y * width + x, b, g, r);
            }
        });

        return result;
    }
}
