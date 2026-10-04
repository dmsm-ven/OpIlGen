using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

/// <summary>
/// Карта смещения: по самому изображению строится карта векторов, и каждый пиксель берётся из точки,
/// сдвинутой на такой вектор. Источник карты: яркость, цвет (R - по X, G - по Y) или оттенок.
/// </summary>
public sealed class DisplacementMapTransformer : PixelTransformerBase
{
    private const string Id = "displacement_map";

    private const double DefaultStrengthPercent = 6;
    private const double DefaultAngleDegrees = 45;
    private const double DefaultMode = 0;
    private const double DefaultBlur = 5;

    private const double DegreesToRadians = Math.PI / 180.0;
    private const float HalfChannel = 127.5f;
    private const int BlurPasses = 2;

    private const int ModeLuminance = 0;
    private const int ModeColor = 1;

    private static readonly TransformerVariable StrengthVariable =
        TransformerVariableFactory.Create(Id, "strength", 0, DefaultStrengthPercent, 25, 0.5);

    private static readonly TransformerVariable AngleVariable =
        TransformerVariableFactory.Create(Id, "angle", 0, DefaultAngleDegrees, 360, 5);

    private static readonly TransformerVariable ModeVariable =
        TransformerVariableFactory.Create(Id, "mode", 0, DefaultMode, 2, 1);

    private static readonly TransformerVariable BlurVariable =
        TransformerVariableFactory.Create(Id, "blur", 0, DefaultBlur, 30, 1);

    public DisplacementMapTransformer(ILocalizationService localizer) : base(localizer)
    {
    }

    public override string Name => Localizer.Get("transformer.displacement_map.name");
    public override string Key => Id;
    public override string Description => Localizer.Get("transformer.displacement_map.description");

    public override TransformerVariable[] AvailableCustomVariables { get; } =
        [StrengthVariable, AngleVariable, ModeVariable, BlurVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int mode = (int)Math.Round(customVariables.GetValue(ModeVariable));
        double angle = customVariables.GetValue(AngleVariable) * DegreesToRadians;
        double strengthPx = customVariables.GetValue(StrengthVariable) / 100.0 * Math.Min(width, height);
        int blur = (int)Math.Round(customVariables.GetValue(BlurVariable));

        int count = width * height;
        var mapX = new float[count];
        var mapY = new float[count];
        float cos = (float)Math.Cos(angle);
        float sin = (float)Math.Sin(angle);

        for (int p = 0, i = 0; p < count; p++, i += 4)
        {
            float b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];

            if (mode == ModeLuminance)
            {
                float lum = BitmapHelper.BlueWeight * b + BitmapHelper.GreenWeight * g + BitmapHelper.RedWeight * r;
                float m = lum / HalfChannel - 1f;
                mapX[p] = m * cos;
                mapY[p] = m * sin;
            }
            else if (mode == ModeColor)
            {
                float dx = r / HalfChannel - 1f;
                float dy = g / HalfChannel - 1f;
                mapX[p] = dx * cos - dy * sin;
                mapY[p] = dx * sin + dy * cos;
            }
            else
            {
                PixelSampler.RgbToHsv(r, g, b, out float hue, out float saturation, out _);
                double a = hue * DegreesToRadians + angle;
                mapX[p] = (float)(Math.Cos(a) * saturation);
                mapY[p] = (float)(Math.Sin(a) * saturation);
            }
        }

        if (blur > 0)
        {
            for (int pass = 0; pass < BlurPasses; pass++)
            {
                BitmapHelper.BoxBlur(mapX, width, height, blur);
                BitmapHelper.BoxBlur(mapY, width, height, blur);
            }
        }

        var result = new byte[pixels.Length];

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int p = y * width + x;
                PixelSampler.SampleBilinear(
                    pixels, width, height, x - mapX[p] * strengthPx, y - mapY[p] * strengthPx, AddressMode.Clamp,
                    out float b, out float g, out float r);
                PixelSampler.SetPixel(result, p, b, g, r);
            }
        });

        return result;
    }
}
