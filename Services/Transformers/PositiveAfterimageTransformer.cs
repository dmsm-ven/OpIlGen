namespace OpIlGen.Services.Transformers;

/// <summary>
/// Позитивный послеобраз: яркое высококонтрастное изображение с насыщенными цветами и точкой фиксации взгляда.
/// После 20-30 секунд пристального взгляда образ какое-то время виден с закрытыми глазами.
/// </summary>
public sealed class PositiveAfterimageTransformer : PixelTransformerBase
{
    /// <summary>Во сколько раз усиливается насыщенность цветов.</summary>
    private const float Saturation = 1.4f;

    // Перцентили яркости для растяжения контраста
    private const double LowPercentile = 0.02;
    private const double HighPercentile = 0.98;

    /// <summary>Минимальный диапазон яркости при растяжении (защита от деления на ноль).</summary>
    private const float MinBrightnessRange = 1f;

    /// <summary>Минимальный радиус точки фиксации, px.</summary>
    private const int MinDotRadius = 4;

    /// <summary>Радиус точки = меньшая сторона изображения / это значение.</summary>
    private const int DotRadiusDivisor = 80;

    /// <summary>Радиус чёрной обводки относительно радиуса точки.</summary>
    private const int OutlineRadiusFactor = 2;

    private const float MaxChannel = 255f;
    private const float RoundingOffset = 0.5f;

    public override string Name => "Позитивный послеобраз (с закрытыми глазами)";
    public override string Key => "afterimage_positive";
    public override string Description =>
        "Затемните комнату, поставьте максимальную яркость экрана и 20-30 секунд смотрите на точку в центре, " +
        "не двигая глаз. Затем закройте глаза и прикройте их ладонями: образ несколько секунд будет виден. " +
        "При дискомфорте прекратите.";

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        var lum = BitmapHelper.ToLuminance(pixels);
        float low = BitmapHelper.Percentile(lum, LowPercentile);
        float high = Math.Max(low + MinBrightnessRange, BitmapHelper.Percentile(lum, HighPercentile));

        var result = new byte[pixels.Length];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            // Растяжение контраста и S-кривая: тёмное уходит в чёрное, светлое в яркое
            float b = Curve(pixels[i], low, high);
            float g = Curve(pixels[i + 1], low, high);
            float r = Curve(pixels[i + 2], low, high);

            // Усиление насыщенности
            float gray = BitmapHelper.BlueWeight * b + BitmapHelper.GreenWeight * g + BitmapHelper.RedWeight * r;
            b = Math.Clamp(gray + (b - gray) * Saturation, 0f, 1f);
            g = Math.Clamp(gray + (g - gray) * Saturation, 0f, 1f);
            r = Math.Clamp(gray + (r - gray) * Saturation, 0f, 1f);

            result[i] = (byte)(b * MaxChannel + RoundingOffset);
            result[i + 1] = (byte)(g * MaxChannel + RoundingOffset);
            result[i + 2] = (byte)(r * MaxChannel + RoundingOffset);
            result[i + 3] = BitmapHelper.Opaque;
        }

        DrawFixationDot(result, width, height);
        return result;
    }

    /// <summary>Нормализация значения в 0..1 и сглаженная S-кривая (smoothstep: 3t² - 2t³).</summary>
    private static float Curve(float value, float low, float high)
    {
        float t = Math.Clamp((value - low) / (high - low), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Белая точка с чёрной обводкой: видна и на тёмном, и на светлом.</summary>
    private static void DrawFixationDot(byte[] px, int w, int h)
    {
        int cx = w / 2, cy = h / 2;
        int r = Math.Max(MinDotRadius, Math.Min(w, h) / DotRadiusDivisor);
        int outer = r * OutlineRadiusFactor;

        for (int y = Math.Max(0, cy - outer); y <= Math.Min(h - 1, cy + outer); y++)
        {
            for (int x = Math.Max(0, cx - outer); x <= Math.Min(w - 1, cx + outer); x++)
            {
                int dx = x - cx, dy = y - cy;
                int d2 = dx * dx + dy * dy;

                if (d2 <= r * r)
                    BitmapHelper.SetGray(px, y * w + x, BitmapHelper.White);
                else if (d2 <= outer * outer)
                    BitmapHelper.SetGray(px, y * w + x, BitmapHelper.Black);
            }
        }
    }
}
