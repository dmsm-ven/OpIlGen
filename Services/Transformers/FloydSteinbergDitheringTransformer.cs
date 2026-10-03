using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

public sealed class FloydSteinbergDitheringTransformer : PixelTransformerBase
{
    /// <summary>Яркость, начиная с которой пиксель становится белым.</summary>
    private const double DefaultThreshold = 128;

    /// <summary>Какая доля ошибки квантования передаётся соседям, %.</summary>
    private const int DefaultDiffusionPercent = 100;

    private const double PercentScale = 100.0;

    // Доли ошибки квантования, передаваемые соседним пикселям
    private const float RightWeight = 7f / 16f;
    private const float BottomLeftWeight = 3f / 16f;
    private const float BottomWeight = 5f / 16f;
    private const float BottomRightWeight = 1f / 16f;

    private static readonly TransformerVariable ThresholdVariable = new()
    {
        NameKey = "transformer.dither_floyd_steinberg.var.threshold.name",
        Key = "threshold",
        DescriptionKey = "transformer.dither_floyd_steinberg.var.threshold.description",
        MinValue = 0,
        DefaultValue = DefaultThreshold,
        MaxValue = BitmapHelper.White,
        Step = 1
    };

    private static readonly TransformerVariable DiffusionVariable = new()
    {
        NameKey = "transformer.dither_floyd_steinberg.var.diffusion_percent.name",
        Key = "diffusion_percent",
        DescriptionKey = "transformer.dither_floyd_steinberg.var.diffusion_percent.description",
        MinValue = 0,
        DefaultValue = DefaultDiffusionPercent,
        MaxValue = 100,
        Step = 5
    };

    public FloydSteinbergDitheringTransformer(ILocalizationService localizer) : base(localizer)
    {
    }

    public override string Name => Localizer.Get("transformer.dither_floyd_steinberg.name");
    public override string Key => "dither_floyd_steinberg";
    public override string Description => Localizer.Get("transformer.dither_floyd_steinberg.description");

    public override TransformerVariable[] AvailableCustomVariables { get; } = [ThresholdVariable, DiffusionVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        float threshold = (float)customVariables.GetValue(ThresholdVariable);
        float diffusion = (float)(customVariables.GetValue(DiffusionVariable) / PercentScale);

        var lum = BitmapHelper.ToLuminance(pixels);
        var result = new byte[pixels.Length];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                float old = lum[idx];
                byte level = old < threshold ? BitmapHelper.Black : BitmapHelper.White;
                float error = (old - level) * diffusion;

                BitmapHelper.SetGray(result, idx, level);

                if (x + 1 < width)
                    lum[idx + 1] += error * RightWeight;

                if (y + 1 < height)
                {
                    if (x > 0)
                        lum[idx + width - 1] += error * BottomLeftWeight;
                    lum[idx + width] += error * BottomWeight;
                    if (x + 1 < width)
                        lum[idx + width + 1] += error * BottomRightWeight;
                }
            }
        }

        return result;
    }
}
