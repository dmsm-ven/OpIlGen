namespace OpIlGen.Services.Transformers;

/// <summary>
/// Позитивный послеобраз: яркое высококонтрастное изображение с насыщенными цветами и точкой фиксации взгляда.
/// После 20-30 секунд пристального взгляда образ какое-то время виден с закрытыми глазами.
/// </summary>
public sealed class PositiveAfterimageTransformer : PixelTransformerBase
{
    /// <summary>Насыщенность цветов, % (100 - без изменений).</summary>
    private const int DefaultSaturationPercent = 140;

    /// <summary>Доля самых тёмных и самых светлых пикселей, отсекаемая при растяжении контраста, %.</summary>
    private const int DefaultClipPercent = 2;

    /// <summary>Радиус точки в промилле от меньшей стороны изображения (12 ‰ ≈ 1/80).</summary>
    private const int DefaultDotSizePermille = 12;

    private const double PercentScale = 100.0;
    private const int PermilleScale = 1000;

    /// <summary>Минимальный диапазон яркости при растяжении (защита от деления на ноль).</summary>
    private const float MinBrightnessRange = 1f;

    /// <summary>Минимальный радиус точки фиксации, px.</summary>
    private const int MinDotRadius = 4;

    /// <summary>Радиус чёрной обводки относительно радиуса точки.</summary>
    private const int OutlineRadiusFactor = 2;

    private const float MaxChannel = 255f;
    private const float RoundingOffset = 0.5f;

    private static readonly TransformerVariable SaturationVariable = new()
    {
        Name = "Насыщенность, %",
        Key = "saturation_percent",
        Description = "Усиление насыщенности цветов. 100 % - без изменений. " +
                      "Яркие насыщенные цвета дают более заметный послеобраз.",
        MinValue = 100,
        DefaultValue = DefaultSaturationPercent,
        MaxValue = 250,
        Step = 10
    };

    private static readonly TransformerVariable ClipVariable = new()
    {
        Name = "Отсечение яркости, %",
        Key = "clip_percent",
        Description = "Какая доля самых тёмных и самых светлых пикселей игнорируется при растяжении контраста. " +
                      "Чем больше значение, тем контрастнее результат.",
        MinValue = 0,
        DefaultValue = DefaultClipPercent,
        MaxValue = 20,
        Step = 1
    };

    private static readonly TransformerVariable DotSizeVariable = new()
    {
        Name = "Размер точки",
        Key = "dot_size",
        Description = "Радиус точки фиксации взгляда в промилле (‰) от меньшей стороны изображения.",
        MinValue = 5,
        DefaultValue = DefaultDotSizePermille,
        MaxValue = 50,
        Step = 1
    };

    public override string Name => "Позитивный послеобраз (с закрытыми глазами)";
    public override string Key => "afterimage_positive";
    public override string Description =>
        "Затемните комнату, поставьте максимальную яркость экрана и 20-30 секунд смотрите на точку в центре, " +
        "не двигая глаз. Затем закройте глаза и прикройте их ладонями: образ несколько секунд будет виден. " +
        "При дискомфорте прекратите.";

    public override TransformerVariable[] AvailableCustomVariables { get; } =
        [SaturationVariable, ClipVariable, DotSizeVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        float saturation = (float)(customVariables.GetValue(SaturationVariable) / PercentScale);
        double clip = customVariables.GetValue(ClipVariable) / PercentScale;
        int dotSize = (int)Math.Round(customVariables.GetValue(DotSizeVariable));

        var lum = BitmapHelper.ToLuminance(pixels);
        float low = BitmapHelper.Percentile(lum, clip);
        float high = Math.Max(low + MinBrightnessRange, BitmapHelper.Percentile(lum, 1.0 - clip));

        var result = new byte[pixels.Length];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            // Растяжение контраста и S-кривая: тёмное уходит в чёрное, светлое в яркое
            float b = Curve(pixels[i], low, high);
            float g = Curve(pixels[i + 1], low, high);
            float r = Curve(pixels[i + 2], low, high);

            // Усиление насыщенности
            float gray = BitmapHelper.BlueWeight * b + BitmapHelper.GreenWeight * g + BitmapHelper.RedWeight * r;
            b = Math.Clamp(gray + (b - gray) * saturation, 0f, 1f);
            g = Math.Clamp(gray + (g - gray) * saturation, 0f, 1f);
            r = Math.Clamp(gray + (r - gray) * saturation, 0f, 1f);

            result[i] = (byte)(b * MaxChannel + RoundingOffset);
            result[i + 1] = (byte)(g * MaxChannel + RoundingOffset);
            result[i + 2] = (byte)(r * MaxChannel + RoundingOffset);
            result[i + 3] = BitmapHelper.Opaque;
        }

        DrawFixationDot(result, width, height, dotSize);
        return result;
    }

    /// <summary>Нормализация значения в 0..1 и сглаженная S-кривая (smoothstep: 3t² - 2t³).</summary>
    private static float Curve(float value, float low, float high)
    {
        float t = Math.Clamp((value - low) / (high - low), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Белая точка с чёрной обводкой: видна и на тёмном, и на светлом.</summary>
    private static void DrawFixationDot(byte[] px, int w, int h, int dotSizePermille)
    {
        int cx = w / 2, cy = h / 2;
        int r = Math.Max(MinDotRadius, Math.Min(w, h) * dotSizePermille / PermilleScale);
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
