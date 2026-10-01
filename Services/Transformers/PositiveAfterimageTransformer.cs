namespace OpIlGen.Services.Transformers;

/// <summary>
/// Позитивный послеобраз: яркое высококонтрастное изображение с насыщенными цветами и точкой фиксации взгляда.
/// После 20-30 секунд пристального взгляда образ какое-то время виден с закрытыми глазами.
/// </summary>
public sealed class PositiveAfterimageTransformer : PixelTransformerBase
{
    private const float Saturation = 1.4f;

    public override string Name => "Позитивный послеобраз (с закрытыми глазами)";
    public override string Key => "afterimage_positive";
    public override string Description =>
        "Затемните комнату, поставьте максимальную яркость экрана и 20-30 секунд смотрите на точку в центре, " +
        "не двигая глаз. Затем закройте глаза и прикройте их ладонями: образ несколько секунд будет виден. " +
        "При дискомфорте прекратите.";

    protected override byte[] Process(byte[] pixels, int width, int height)
    {
        var lum = BitmapHelper.ToLuminance(pixels);
        float low = BitmapHelper.Percentile(lum, 0.02);
        float high = Math.Max(low + 1f, BitmapHelper.Percentile(lum, 0.98));

        var result = new byte[pixels.Length];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            // Растяжение контраста и S-кривая: тёмное уходит в чёрное, светлое в яркое
            float b = Curve(pixels[i], low, high);
            float g = Curve(pixels[i + 1], low, high);
            float r = Curve(pixels[i + 2], low, high);

            // Усиление насыщенности
            float gray = 0.114f * b + 0.587f * g + 0.299f * r;
            b = Math.Clamp(gray + (b - gray) * Saturation, 0f, 1f);
            g = Math.Clamp(gray + (g - gray) * Saturation, 0f, 1f);
            r = Math.Clamp(gray + (r - gray) * Saturation, 0f, 1f);

            result[i] = (byte)(b * 255f + 0.5f);
            result[i + 1] = (byte)(g * 255f + 0.5f);
            result[i + 2] = (byte)(r * 255f + 0.5f);
            result[i + 3] = 255;
        }

        DrawFixationDot(result, width, height);
        return result;
    }

    private static float Curve(float value, float low, float high)
    {
        float t = Math.Clamp((value - low) / (high - low), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Белая точка с чёрной обводкой: видна и на тёмном, и на светлом.</summary>
    private static void DrawFixationDot(byte[] px, int w, int h)
    {
        int cx = w / 2, cy = h / 2;
        int r = Math.Max(4, Math.Min(w, h) / 80);
        int outer = r * 2;

        for (int y = Math.Max(0, cy - outer); y <= Math.Min(h - 1, cy + outer); y++)
        {
            for (int x = Math.Max(0, cx - outer); x <= Math.Min(w - 1, cx + outer); x++)
            {
                int dx = x - cx, dy = y - cy;
                int d2 = dx * dx + dy * dy;

                if (d2 <= r * r)
                    BitmapHelper.SetGray(px, y * w + x, 255);
                else if (d2 <= outer * outer)
                    BitmapHelper.SetGray(px, y * w + x, 0);
            }
        }
    }
}
