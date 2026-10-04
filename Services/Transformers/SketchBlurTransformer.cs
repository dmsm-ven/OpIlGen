using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

/// <summary>Soft charcoal/pencil treatment: blurred grayscale paper with configurable dark edge strokes.</summary>
public sealed class SketchBlurTransformer : PixelTransformerBase
{
    private static readonly TransformerVariable BlurRadius = new()
    {
        Key = "blur_radius", NameKey = "transformer.sketch_blur.var.blur_radius.name",
        DescriptionKey = "transformer.sketch_blur.var.blur_radius.description",
        MinValue = 1, DefaultValue = 2, MaxValue = 12, Step = 1
    };
    private static readonly TransformerVariable EdgeStrength = new()
    {
        Key = "edge_strength", NameKey = "transformer.sketch_blur.var.edge_strength.name",
        DescriptionKey = "transformer.sketch_blur.var.edge_strength.description",
        MinValue = 0, DefaultValue = 75, MaxValue = 200, Step = 1
    };
    private static readonly TransformerVariable Contrast = new()
    {
        Key = "contrast", NameKey = "transformer.sketch_blur.var.contrast.name",
        DescriptionKey = "transformer.sketch_blur.var.contrast.description",
        MinValue = 50, DefaultValue = 115, MaxValue = 200, Step = 1
    };
    private static readonly TransformerVariable PaperBrightness = new()
    {
        Key = "paper_brightness", NameKey = "transformer.sketch_blur.var.paper_brightness.name",
        DescriptionKey = "transformer.sketch_blur.var.paper_brightness.description",
        MinValue = 150, DefaultValue = 245, MaxValue = 255, Step = 1
    };

    public SketchBlurTransformer(ILocalizationService localizer) : base(localizer) { }
    public override string Name => Localizer.Get("transformer.sketch_blur.name");
    public override string Key => "sketch_blur";
    public override string Description => Localizer.Get("transformer.sketch_blur.description");
    public override TransformerVariable[] AvailableCustomVariables { get; } = [BlurRadius, EdgeStrength, Contrast, PaperBrightness];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int radius = (int)Math.Round(customVariables.GetValue(BlurRadius));
        double edgeStrength = customVariables.GetValue(EdgeStrength) / 100.0;
        double contrast = customVariables.GetValue(Contrast) / 100.0;
        double paper = customVariables.GetValue(PaperBrightness);
        var lum = BitmapHelper.ToLuminance(pixels);
        var smooth = (float[])lum.Clone();
        BitmapHelper.BoxBlur(smooth, width, height, radius);

        var result = new byte[pixels.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int p = y * width + x;
            int xm = Math.Max(0, x - 1), xp = Math.Min(width - 1, x + 1);
            int ym = Math.Max(0, y - 1), yp = Math.Min(height - 1, y + 1);
            double gx = smooth[y * width + xp] - smooth[y * width + xm];
            double gy = smooth[yp * width + x] - smooth[ym * width + x];
            double edge = Math.Sqrt(gx * gx + gy * gy) / 4.0;
            double baseTone = paper - (255.0 - smooth[p]) * (0.45 + contrast * 0.55);
            double tone = baseTone - edge * edgeStrength;
            byte value = (byte)Math.Clamp((int)Math.Round(tone), 0, 255);
            BitmapHelper.SetGray(result, p, value);
        }
        return result;
    }
}
