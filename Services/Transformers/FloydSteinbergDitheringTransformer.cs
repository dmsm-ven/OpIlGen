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
        Name = "Порог яркости",
        Key = "threshold",
        Description = "Яркость, начиная с которой пиксель становится белым. " +
                      "Чем больше значение, тем темнее результат.",
        MinValue = 0,
        DefaultValue = DefaultThreshold,
        MaxValue = BitmapHelper.White,
        Step = 1
    };

    private static readonly TransformerVariable DiffusionVariable = new()
    {
        Name = "Распространение ошибки, %",
        Key = "diffusion_percent",
        Description = "Какая доля ошибки квантования передаётся соседним пикселям. " +
                      "100 % - классический дизеринг, меньшие значения дают более контрастное, «жёсткое» изображение.",
        MinValue = 0,
        DefaultValue = DefaultDiffusionPercent,
        MaxValue = 100,
        Step = 5
    };

    public override string Name => "Дизеринг Флойда-Стейнберга";
    public override string Key => "dither_floyd_steinberg";
    public override string Description =>
        "Только чёрный и белый цвета, но ошибка квантования распределяется по соседним пикселям, " +
        "поэтому глаз «видит» плавные полутона.";

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
