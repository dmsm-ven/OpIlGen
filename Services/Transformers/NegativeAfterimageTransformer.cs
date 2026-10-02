namespace OpIlGen.Services.Transformers;

public sealed class NegativeAfterimageTransformer : PixelTransformerBase
{
    /// <summary>Минимальный радиус точки фиксации, px.</summary>
    private const int MinDotRadius = 5;

    /// <summary>Радиус точки в промилле от меньшей стороны изображения (17 ‰ ≈ 1/60).</summary>
    private const int DefaultDotSizePermille = 17;

    private const int PermilleScale = 1000;

    /// <summary>Радиус белой обводки относительно радиуса точки.</summary>
    private const double OutlineRadiusFactor = 1.5;

    // Цвет точки фиксации: красный (порядок BGR)
    private const byte DotBlue = 0;
    private const byte DotGreen = 0;
    private const byte DotRed = 255;

    private static readonly TransformerVariable DotSizeVariable = new()
    {
        Name = "Размер точки",
        Key = "dot_size",
        Description = "Радиус красной точки фиксации взгляда в промилле (‰) от меньшей стороны изображения.",
        MinValue = 5,
        DefaultValue = DefaultDotSizePermille,
        MaxValue = 50,
        Step = 1
    };

    public override string Name => "Негативное послесвечение";
    public override string Key => "afterimage";
    public override string Description =>
        "Смотрите на красную точку в центре 20-30 секунд, не двигая глаз, затем переведите взгляд " +
        "на белую стену или пустой лист: вы увидите исходное изображение в нормальных цветах.";

    public override TransformerVariable[] AvailableCustomVariables { get; } = [DotSizeVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int dotSize = (int)Math.Round(customVariables.GetValue(DotSizeVariable));
        var result = new byte[pixels.Length];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            result[i] = (byte)(BitmapHelper.White - pixels[i]);
            result[i + 1] = (byte)(BitmapHelper.White - pixels[i + 1]);
            result[i + 2] = (byte)(BitmapHelper.White - pixels[i + 2]);
            result[i + 3] = BitmapHelper.Opaque;
        }

        DrawFixationDot(result, width, height, dotSize);
        return result;
    }

    private static void DrawFixationDot(byte[] px, int w, int h, int dotSizePermille)
    {
        int cx = w / 2, cy = h / 2;
        int r = Math.Max(MinDotRadius, Math.Min(w, h) * dotSizePermille / PermilleScale);
        int outer = (int)(r * OutlineRadiusFactor);

        for (int y = Math.Max(0, cy - outer); y <= Math.Min(h - 1, cy + outer); y++)
        {
            for (int x = Math.Max(0, cx - outer); x <= Math.Min(w - 1, cx + outer); x++)
            {
                int dx = x - cx, dy = y - cy;
                int d2 = dx * dx + dy * dy;
                int i = (y * w + x) * 4;

                if (d2 <= r * r)
                {
                    px[i] = DotBlue;
                    px[i + 1] = DotGreen;
                    px[i + 2] = DotRed;
                    px[i + 3] = BitmapHelper.Opaque;
                }
                else if (d2 <= outer * outer)
                {
                    BitmapHelper.SetGray(px, y * w + x, BitmapHelper.White); // белая обводка
                }
            }
        }
    }
}
