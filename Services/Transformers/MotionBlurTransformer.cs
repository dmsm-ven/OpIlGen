using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

/// <summary>Directional motion blur made by averaging samples along a configurable vector.</summary>
public sealed class MotionBlurTransformer : PixelTransformerBase
{
    private static readonly TransformerVariable Distance = new()
    {
        Key = "distance", NameKey = "transformer.motion_blur.var.distance.name",
        DescriptionKey = "transformer.motion_blur.var.distance.description",
        MinValue = 1, DefaultValue = 18, MaxValue = 100, Step = 1
    };
    private static readonly TransformerVariable Angle = new()
    {
        Key = "angle", NameKey = "transformer.motion_blur.var.angle.name",
        DescriptionKey = "transformer.motion_blur.var.angle.description",
        MinValue = 0, DefaultValue = 0, MaxValue = 359, Step = 1
    };
    private static readonly TransformerVariable Samples = new()
    {
        Key = "samples", NameKey = "transformer.motion_blur.var.samples.name",
        DescriptionKey = "transformer.motion_blur.var.samples.description",
        MinValue = 3, DefaultValue =  nine, MaxValue = 41, Step = 2
    };
    private static readonly TransformerVariable Strength = new()
    {
        Key = "strength", NameKey = "transformer.motion_blur.var.strength.name",
        DescriptionKey = "transformer.motion_blur.var.strength.description",
        MinValue = 0, DefaultValue = 100, MaxValue = 100, Step = 1
    };

    public MotionBlurTransformer(ILocalizationService localizer) : base(localizer) { }

    public override string Name => Localizer.Get("transformer.motion_blur.name");
    public override string Key => "motion_blur";
    public override string Description => Localizer.Get("transformer.motion_blur.description");
    public override TransformerVariable[] AvailableCustomVariables { get; } = [Distance, Angle, Samples, Strength];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int distance = (int)Math.Round(customVariables.GetValue(Distance));
        double angle = customVariables.GetValue(Angle) * Math.PI / 180.0;
        int samples = (int)Math.Round(customVariables.GetValue(Samples));
        double amount = customVariables.GetValue(Strength) / 100.0;
        double dx = Math.Cos(angle) * distance / 2.0;
        double dy = Math.Sin(angle) * distance / 2.0;

        var result = (byte[])pixels.Clone();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int dst = (y * width + x) * 4;
            double b = 0, g = 0, r = 0;
            for (int s = 0; s < samples; s++)
            {
                double t = samples == 1 ? 0 : (double)s / (samples - 1) - 0.5;
                int sx = Math.Clamp((int)Math.Round(x + dx * t * 2), 0, width - 1);
                int sy = Math.Clamp((int)Math.Round(y + dy * t * 2), 0, height - 1);
                int si = (sy * width + sx) * 4;
                b += pixels[si]; g += pixels[si + 1]; r += pixels[si + 2];
            }
            result[dst] = BlurTransformerUtilities.Blend(pixels[dst], (byte)Math.Round(b / samples), amount);
            result[dst + 1] = BlurTransformerUtilities.Blend(pixels[dst + 1], (byte)Math.Round(g / samples), amount);
            result[dst + 2] = BlurTransformerUtilities.Blend(pixels[dst + 2], (byte)Math.Round(r / samples), amount);
            result[dst + 3] = 255;
        }
        return result;
    }

    private const int nine = 9;
}
