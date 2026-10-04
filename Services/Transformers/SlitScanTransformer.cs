using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

/// <summary>
/// Slit-scan: изображение режется на горизонтальные полосы, каждая полоса сдвигается и растягивается
/// по волне (синусоиде) вдоль высоты. Края сдвинутой полосы переходят на противоположную сторону.
/// </summary>
public sealed class SlitScanTransformer : PixelTransformerBase
{
    private const string Id = "slit_scan";

    private const double DefaultStrips = 40;
    private const double DefaultShiftPercent = 15;
    private const double DefaultStretchPercent = 30;
    private const double DefaultWaves = 3;

    /// <summary>Во сколько раз максимум можно растянуть/сжать полосу при растяжении 100%.</summary>
    private const double MaxStretchFactor = 0.9;

    private static readonly TransformerVariable StripsVariable =
        TransformerVariableFactory.Create(Id, "strips", 4, DefaultStrips, 200, 1);

    private static readonly TransformerVariable ShiftVariable =
        TransformerVariableFactory.Create(Id, "shift", 0, DefaultShiftPercent, 50, 1);

    private static readonly TransformerVariable StretchVariable =
        TransformerVariableFactory.Create(Id, "stretch", 0, DefaultStretchPercent, 100, 5);

    private static readonly TransformerVariable WavesVariable =
        TransformerVariableFactory.Create(Id, "waves", 0, DefaultWaves, 12, 0.25);

    public SlitScanTransformer(ILocalizationService localizer) : base(localizer)
    {
    }

    public override string Name => Localizer.Get("transformer.slit_scan.name");
    public override string Key => Id;
    public override string Description => Localizer.Get("transformer.slit_scan.description");

    public override TransformerVariable[] AvailableCustomVariables { get; } =
        [StripsVariable, ShiftVariable, StretchVariable, WavesVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int strips = (int)Math.Round(customVariables.GetValue(StripsVariable));
        double shift = customVariables.GetValue(ShiftVariable) / 100.0 * width;
        double stretch = customVariables.GetValue(StretchVariable) / 100.0;
        double waves = customVariables.GetValue(WavesVariable);

        double centerX = (width - 1) / 2.0;
        var result = new byte[pixels.Length];

        Parallel.For(0, height, y =>
        {
            int strip = Math.Min(strips - 1, (int)((long)y * strips / height));
            double phase = 2 * Math.PI * waves * (strip + 0.5) / strips;
            double offset = shift * Math.Sin(phase);
            double scale = 1 + MaxStretchFactor * stretch * Math.Cos(phase);

            for (int x = 0; x < width; x++)
            {
                double sourceX = centerX + (x - centerX - offset) / scale;
                PixelSampler.SampleBilinear(pixels, width, height, sourceX, y, AddressMode.Wrap,
                    out float b, out float g, out float r);
                PixelSampler.SetPixel(result, y * width + x, b, g, r);
            }
        });

        return result;
    }
}
