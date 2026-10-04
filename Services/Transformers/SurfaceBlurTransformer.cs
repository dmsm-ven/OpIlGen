using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

/// <summary>Edge-preserving bilateral smoothing: similar neighboring colors are averaged more strongly.</summary>
public sealed class SurfaceBlurTransformer : PixelTransformerBase
{
    private static readonly TransformerVariable Radius = new()
    {
        Key = "radius", NameKey = "transformer.surface_blur.var.radius.name",
        DescriptionKey = "transformer.surface_blur.var.radius.description",
        MinValue = 1, DefaultValue = 4, MaxValue = 12, Step = 1
    };
    private static readonly TransformerVariable EdgeThreshold = new()
    {
        Key = "edge_threshold", NameKey = "transformer.surface_blur.var.edge_threshold.name",
        DescriptionKey = "transformer.surface_blur.var.edge_threshold.description",
        MinValue = 1, DefaultValue = 35, MaxValue = 120, Step = 1
    };
    private static readonly TransformerVariable Strength = new()
    {
        Key = "strength", NameKey = "transformer.surface_blur.var.strength.name",
        DescriptionKey = "transformer.surface_blur.var.strength.description",
        MinValue = 0, DefaultValue = 85, MaxValue = 100, Step = 1
    };
    private static readonly TransformerVariable SpatialWeight = new()
    {
        Key = "spatial_weight", NameKey = "transformer.surface_blur.var.spatial_weight.name",
        DescriptionKey = "transformer.surface_blur.var.spatial_weight.description",
        MinValue = 1, DefaultValue = 20, MaxValue = 100, Step = 1
    };

    public SurfaceBlurTransformer(ILocalizationService localizer) : base(localizer) { }
    public override string Name => Localizer.Get("transformer.surface_blur.name");
    public override string Key => "surface_blur";
    public override string Description => Localizer.Get("transformer.surface_blur.description");
    public override TransformerVariable[] AvailableCustomVariables { get; } = [Radius, EdgeThreshold, Strength, SpatialWeight];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int radius = (int)Math.Round(customVariables.GetValue(Radius));
        double edge = customVariables.GetValue(EdgeThreshold);
        double strength = customVariables.GetValue(Strength) / 100.0;
        double spatial = customVariables.GetValue(SpatialWeight) / 100.0;
        var output = (byte[])pixels.Clone();

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int p = y * width + x, i = p * 4;
            double sumB = 0, sumG = 0, sumR = 0, sumW = 0;
            float centerLum = BlurTransformerUtilities.Luminance(pixels, p);

            for (int oy = -radius; oy <= radius; oy++)
            for (int ox = -radius; ox <= radius; ox++)
            {
                int sx = Math.Clamp(x + ox, 0, width - 1);
                int sy = Math.Clamp(y + oy, 0, height - 1);
                int q = sy * width + sx, qi = q * 4;
                double distance2 = ox * ox + oy * oy;
                double spatialWeight = Math.Exp(-distance2 / (2.0 * radius * radius * spatial + 0.0001));
                double difference = BlurTransformerUtilities.Luminance(pixels, q) - centerLum;
                double rangeWeight = Math.Exp(-(difference * difference) / (2.0 * edge * edge));
                double w = spatialWeight * rangeWeight;
                sumB += pixels[qi] * w;
                sumG += pixels[qi + 1] * w;
                sumR += pixels[qi + 2] * w;
                sumW += w;
            }

            if (sumW <= 0) continue;
            output[i] = BlurTransformerUtilities.Blend(pixels[i], (byte)Math.Round(sumB / sumW), strength);
            output[i + 1] = BlurTransformerUtilities.Blend(pixels[i + 1], (byte)Math.Round(sumG / sumW), strength);
            output[i + 2] = BlurTransformerUtilities.Blend(pixels[i + 2], (byte)Math.Round(sumR / sumW), strength);
            output[i + 3] = 255;
        }
        return output;
    }
}
