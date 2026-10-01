namespace OpIlGen.Services.Transformers;

public sealed class NegativeAfterimageTransformer : PixelTransformerBase
{
    public override string Name => "Негативное послесвечение";
    public override string Key => "afterimage";
    public override string Description =>
        "Смотрите на красную точку в центре 20-30 секунд, не двигая глаз, затем переведите взгляд " +
        "на белую стену или пустой лист: вы увидите исходное изображение в нормальных цветах.";

    protected override byte[] Process(byte[] pixels, int width, int height)
    {
        var result = new byte[pixels.Length];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            result[i] = (byte)(255 - pixels[i]);
            result[i + 1] = (byte)(255 - pixels[i + 1]);
            result[i + 2] = (byte)(255 - pixels[i + 2]);
            result[i + 3] = 255;
        }

        DrawFixationDot(result, width, height);
        return result;
    }

    private static void DrawFixationDot(byte[] px, int w, int h)
    {
        int cx = w / 2, cy = h / 2;
        int r = Math.Max(5, Math.Min(w, h) / 60);
        int outer = r * 3 / 2;

        for (int y = Math.Max(0, cy - outer); y <= Math.Min(h - 1, cy + outer); y++)
        {
            for (int x = Math.Max(0, cx - outer); x <= Math.Min(w - 1, cx + outer); x++)
            {
                int dx = x - cx, dy = y - cy;
                int d2 = dx * dx + dy * dy;
                int i = (y * w + x) * 4;

                if (d2 <= r * r)
                {
                    px[i] = 0; px[i + 1] = 0; px[i + 2] = 255; px[i + 3] = 255; // красный
                }
                else if (d2 <= outer * outer)
                {
                    px[i] = 255; px[i + 1] = 255; px[i + 2] = 255; px[i + 3] = 255; // белая обводка
                }
            }
        }
    }
}
