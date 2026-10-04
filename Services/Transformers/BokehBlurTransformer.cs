using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

/// <summary>Soft-focus blur with bright circular bokeh highlights sampled from bright image regions.</summary>
public sealed class BokehBlurTransformer : PixelTransformerBase
{
    private static readonly TransformerVariable BlurRadius = new()
    {
        Key = "blur_radius",
        NameKey = "transformer.bokeh_blur.var.blur_radius.name",
        DescriptionKey = "transformer.bokeh_blur.var.blur_radius.description",
        MinValue = 1,
        DefaultValue = 8,
        MaxValue = 35,
        Step = 1
    };
    private static readonly TransformerVariable CellSize = new()
    {
        Key = "cell_size",
        NameKey = "transformer.bokeh_blur.var.cell_size.name",
        DescriptionKey = "transformer.bokeh_blur.var.cell_size.description",
        MinValue = 8,
        DefaultValue = 36,
        MaxValue = 120,
        Step = 1
    };
    private static readonly TransformerVariable HighlightThreshold = new()
    {
        Key = "highlight_threshold",
        NameKey = "transformer.bokeh_blur.var.highlight_threshold.name",
        DescriptionKey = "transformer.bokeh_blur.var.highlight_threshold.description",
        MinValue = 80,
        DefaultValue = 190,
        MaxValue = 250,
        Step = 1
    };
    private static readonly TransformerVariable HighlightStrength = new()
    {
        Key = "highlight_strength",
        NameKey = "transformer.bokeh_blur.var.highlight_strength.name",
        DescriptionKey = "transformer.bokeh_blur.var.highlight_strength.description",
        MinValue = 0,
        DefaultValue = 55,
        MaxValue = 100,
        Step = 1
    };

    public BokehBlurTransformer(ILocalizationService localizer) : base(localizer) { }
    public override string Name => Localizer.Get("transformer.bokeh_blur.name");
    public override string Key => "bokeh_blur";
    public override string Description => Localizer.Get("transformer.bokeh_blur.description");
    public override TransformerVariable[] AvailableCustomVariables { get; } = [BlurRadius, CellSize, HighlightThreshold, HighlightStrength];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int radius = (int)Math.Round(customVariables.GetValue(BlurRadius));
        int cell = (int)Math.Round(customVariables.GetValue(CellSize));
        double threshold = customVariables.GetValue(HighlightThreshold);
        double strength = customVariables.GetValue(HighlightStrength) / 100.0;
        var result = BlurTransformerUtilities.BoxBlurPixels(pixels, width, height, radius);

        // One soft circular highlight is considered per grid cell. Its color comes from the brightest
        // source pixel near the cell center, avoiding random/noisy placement.
        for (int cy = cell / 2; cy < height; cy += cell)
            for (int cx = cell / 2; cx < width; cx += cell)
            {
                int best = -1;
                float bestLum = (float)threshold;
                int search = Math.Max(1, cell / 3);
                for (int oy = -search; oy <= search; oy += Math.Max(1, search / 3))
                    for (int ox = -search; ox <= search; ox += Math.Max(1, search / 3))
                    {
                        int x = Math.Clamp(cx + ox, 0, width - 1);
                        int y = Math.Clamp(cy + oy, 0, height - 1);
                        int p = y * width + x;
                        float lum = BlurTransformerUtilities.Luminance(pixels, p);
                        if (lum > bestLum) { bestLum = lum; best = p; }
                    }
                if (best < 0) continue;

                int bi = best * 4;
                byte cb = pixels[bi], cg = pixels[bi + 1], cr = pixels[bi + 2];
                int discRadius = Math.Max(2, cell / 3);
                int left = Math.Max(0, cx - discRadius), right = Math.Min(width - 1, cx + discRadius);
                int top = Math.Max(0, cy - discRadius), bottom = Math.Min(height - 1, cy + discRadius);

                for (int y = top; y <= bottom; y++)
                    for (int x = left; x <= right; x++)
                    {
                        double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / discRadius;
                        if (d >= 1) continue;
                        double falloff = (1.0 - d * d) * strength * (bestLum - threshold) / Math.Max(1.0, 255.0 - threshold);
                        double a = BlurTransformerUtilities.Clamp01(falloff);
                        int i = (y * width + x) * 4;
                        result[i] = BlurTransformerUtilities.Blend(result[i], cb, a);
                        result[i + 1] = BlurTransformerUtilities.Blend(result[i + 1], cg, a);
                        result[i + 2] = BlurTransformerUtilities.Blend(result[i + 2], cr, a);
                    }
            }
        return result;
    }
}
