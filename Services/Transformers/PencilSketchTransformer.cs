using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

/// <summary>
/// Рисунок карандашем: классический алгоритм «осветление основы» (color dodge).
/// Серое изображение делится на размытую инвертированную копию - плоские области становятся белой бумагой,
/// а на контурах и в тенях остаются штрихи.
/// </summary>
public sealed class PencilSketchTransformer : PixelTransformerBase
{
    /// <summary>Размер грифеля: радиус размытия в пикселях (больше - толще и мягче штрихи).</summary>
    private const int DefaultTipSize = 2;

    /// <summary>Диапазон тонов: смещение яркости рисунка (больше нуля - темнее, меньше - светлее).</summary>
    private const int DefaultColorRange = 0;

    /// <summary>Радиус одного прохода размытия относительно размера грифеля.</summary>
    private const double BlurRadiusFactor = 0.6;

    /// <summary>Несколько проходов «прямоугольного» размытия дают результат, близкий к гауссову.</summary>
    private const int BlurPasses = 3;

    private const float MaxLuminance = 255f;

    /// <summary>
    /// Добавка к делимому и делителю при «осветлении основы»: без неё на очень тёмных шумных пикселях деление
    /// даёт случайные чёрно-белые точки. На светлых областях добавка почти не влияет.
    /// </summary>
    private const float NoiseGuard = 8f;

    /// <summary>
    /// Сила «штриховки»: тёмные области рисунка слегка затемняются (0 - чистые контуры на белой бумаге,
    /// больше - заметнее тоновая растушёвка и серая бумага).
    /// </summary>
    private const float ShadingStrength = 0.25f;

    /// <summary>Каждые GammaStepDivisor единиц диапазона тонов гамма меняется в GammaBase раз.</summary>
    private const double GammaBase = 2.0;
    private const double GammaStepDivisor = 10.0;

    private const double RoundingOffset = 0.5;

    private static readonly TransformerVariable TipSizeVariable = new()
    {
        NameKey = "transformer.pencil_sketch.var.tip_size.name",
        Key = "tip_size",
        DescriptionKey = "transformer.pencil_sketch.var.tip_size.description",
        MinValue = 1,
        DefaultValue = DefaultTipSize,
        MaxValue = 20,
        Step = 1
    };

    private static readonly TransformerVariable ColorRangeVariable = new()
    {
        NameKey = "transformer.pencil_sketch.var.color_range.name",
        Key = "color_range",
        DescriptionKey = "transformer.pencil_sketch.var.color_range.description",
        MinValue = -20,
        DefaultValue = DefaultColorRange,
        MaxValue = 20,
        Step = 1
    };

    public PencilSketchTransformer(ILocalizationService localizer) : base(localizer)
    {
    }

    public override string Name => Localizer.Get("transformer.pencil_sketch.name");
    public override string Key => "pencil_sketch";
    public override string Description => Localizer.Get("transformer.pencil_sketch.description");

    public override TransformerVariable[] AvailableCustomVariables { get; } = [TipSizeVariable, ColorRangeVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int tipSize = (int)Math.Round(customVariables.GetValue(TipSizeVariable));
        double colorRange = customVariables.GetValue(ColorRangeVariable);

        var gray = BitmapHelper.ToLuminance(pixels);

        // Инвертированная копия, размытая на величину грифеля
        var blurred = new float[gray.Length];
        for (int i = 0; i < gray.Length; i++)
        {
            blurred[i] = MaxLuminance - gray[i];
        }

        int radius = Math.Max(1, (int)Math.Round(tipSize * BlurRadiusFactor));
        for (int pass = 0; pass < BlurPasses; pass++)
        {
            BitmapHelper.BoxBlur(blurred, width, height, radius);
        }

        // Таблица тона: гамма делает рисунок темнее или светлее
        double gamma = Math.Pow(GammaBase, colorRange / GammaStepDivisor);
        var tone = new byte[(int)MaxLuminance + 1];
        for (int v = 0; v < tone.Length; v++)
        {
            tone[v] = (byte)(BitmapHelper.White * Math.Pow(v / (double)MaxLuminance, gamma) + RoundingOffset);
        }

        var result = new byte[pixels.Length];

        for (int p = 0; p < gray.Length; p++)
        {
            // Color dodge: серое / (1 - размытая инвертированная копия) = серое / размытое серое
            float blurredGray = MaxLuminance - blurred[p];
            float dodge = Math.Min(1f, (gray[p] + NoiseGuard) / (blurredGray + NoiseGuard));

            // Лёгкая штриховка: чем темнее окрестность, тем темнее штрих
            float shaded = dodge * (1f - ShadingStrength * (1f - blurredGray / MaxLuminance));

            int level = Math.Clamp((int)(shaded * MaxLuminance + (float)RoundingOffset), 0, (int)MaxLuminance);
            BitmapHelper.SetGray(result, p, tone[level]);
        }

        return result;
    }
}
