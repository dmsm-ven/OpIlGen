using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

/// <summary>
/// Линейный оп-арт: изображение из параллельных чёрных линий. Яркость сдвигает линии (они изгибаются
/// и повторяют контуры) и меняет их толщину: чем темнее участок, тем линия толще.
/// </summary>
public sealed class OpArtLinesTransformer : PixelTransformerBase
{
    private const string Id = "op_art_lines";

    private const double DefaultLineCount = 60;
    private const double DefaultAngleDegrees = 0;
    private const double DefaultBendPercent = 60;
    private const double DefaultThicknessPercent = 85;

    private const double DegreesToRadians = Math.PI / 180.0;
    private const double MinSpacingPx = 2;
    private const double MaxLuminance = 255.0;
    private const int BlurPasses = 2;

    /// <summary>Толщина линии на самом светлом месте (доля от максимальной).</summary>
    private const double MinFill = 0.05;

    private static readonly TransformerVariable LineCountVariable =
        TransformerVariableFactory.Create(Id, "line_count", 10, DefaultLineCount, 200, 1);

    private static readonly TransformerVariable AngleVariable =
        TransformerVariableFactory.Create(Id, "angle", 0, DefaultAngleDegrees, 180, 5);

    private static readonly TransformerVariable BendVariable =
        TransformerVariableFactory.Create(Id, "bend", 0, DefaultBendPercent, 200, 5);

    private static readonly TransformerVariable ThicknessVariable =
        TransformerVariableFactory.Create(Id, "thickness", 10, DefaultThicknessPercent, 100, 5);

    public OpArtLinesTransformer(ILocalizationService localizer) : base(localizer)
    {
    }

    public override string Name => Localizer.Get("transformer.op_art_lines.name");
    public override string Key => Id;
    public override string Description => Localizer.Get("transformer.op_art_lines.description");

    public override TransformerVariable[] AvailableCustomVariables { get; } =
        [LineCountVariable, AngleVariable, BendVariable, ThicknessVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int lineCount = (int)Math.Round(customVariables.GetValue(LineCountVariable));
        double angle = customVariables.GetValue(AngleVariable) * DegreesToRadians;
        double bend = customVariables.GetValue(BendVariable) / 100.0;
        double thickness = customVariables.GetValue(ThicknessVariable) / 100.0;

        double spacing = Math.Max(MinSpacingPx, Math.Min(width, height) / (double)lineCount);

        // Лёгкое размытие: линии и толщина меняются плавно, без «шума» мелких деталей
        var lum = BitmapHelper.ToLuminance(pixels);
        int blurRadius = Math.Max(1, (int)(spacing * 0.25));
        for (int pass = 0; pass < BlurPasses; pass++)
        {
            BitmapHelper.BoxBlur(lum, width, height, blurRadius);
        }

        // Нормаль к линиям: u - расстояние от начала координат поперёк линий
        double normalX = -Math.Sin(angle);
        double normalY = Math.Cos(angle);

        var result = new byte[pixels.Length];

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int p = y * width + x;
                double brightness = lum[p] / MaxLuminance;
                double darkness = 1.0 - brightness;

                // Положение в «периодах» линий: яркость сдвигает линию, поэтому она изгибается
                double u = x * normalX + y * normalY;
                double position = u / spacing + bend * (brightness - 0.5) * 2.0;

                double phase = position - Math.Floor(position);
                double distance = Math.Abs(phase - 0.5);

                double fill = thickness * (MinFill + (1 - MinFill) * darkness);
                double coverage = Math.Clamp((fill * 0.5 - distance) * spacing + 0.5, 0, 1);

                BitmapHelper.SetGray(result, p, (byte)Math.Round(MaxLuminance * (1 - coverage)));
            }
        });

        return result;
    }
}
