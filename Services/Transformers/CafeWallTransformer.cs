namespace OpIlGen.Services.Transformers;

public sealed class CafeWallTransformer : PixelTransformerBase
{
    /// <summary>Сколько плиток (по высоте ряда) умещается вдоль меньшей стороны изображения.</summary>
    private const int DefaultTilesAcross = 16;

    /// <summary>Насколько сильно плитки затемняют/осветляют изображение, %.</summary>
    private const int DefaultContrastPercent = 75;

    /// <summary>Сдвиг нечётных рядов в процентах от размера плитки.</summary>
    private const int DefaultRowShiftPercent = 50;

    private const int PercentScale = 100;

    /// <summary>Минимальный размер плитки, px.</summary>
    private const int MinTileSize = 16;

    /// <summary>Минимальная толщина шва, px.</summary>
    private const int MinMortarWidth = 2;

    /// <summary>Толщина шва = размер плитки / это значение.</summary>
    private const int MortarWidthDivisor = 12;

    /// <summary>Серый цвет шва.</summary>
    private const byte MortarGray = 128;

    /// <summary>Тёмная и светлая плитки чередуются: период узора - 2 плитки.</summary>
    private const int TilesPerPeriod = 2;

    /// <summary>Узор повторяется через строку: чётные и нечётные ряды смещены.</summary>
    private const int RowsPerPattern = 2;

    private static readonly TransformerVariable TilesAcrossVariable = new()
    {
        Name = "Плиток по меньшей стороне",
        Key = "tiles_across",
        Description = "Сколько рядов плиток умещается вдоль меньшей стороны изображения.",
        MinValue = 6,
        DefaultValue = DefaultTilesAcross,
        MaxValue = 40,
        Step = 1
    };

    private static readonly TransformerVariable ContrastVariable = new()
    {
        Name = "Контраст плиток, %",
        Key = "contrast_percent",
        Description = "Насколько сильно тёмные и светлые плитки перекрывают исходное изображение. " +
                      "Чем больше значение, тем отчётливее иллюзия, но тем хуже видно картинку.",
        MinValue = 10,
        DefaultValue = DefaultContrastPercent,
        MaxValue = 100,
        Step = 5
    };

    private static readonly TransformerVariable RowShiftVariable = new()
    {
        Name = "Сдвиг рядов, %",
        Key = "row_shift_percent",
        Description = "На сколько (в процентах от размера плитки) сдвигается каждый второй ряд. " +
                      "Иллюзия наклона швов сильнее всего при сдвиге около 25-50 %.",
        MinValue = 0,
        DefaultValue = DefaultRowShiftPercent,
        MaxValue = 100,
        Step = 5
    };

    public override string Name => "Кафе-стена";
    public override string Key => "cafe_wall";
    public override string Description =>
        "Ряды светлых и тёмных плиток, сдвинутых друг относительно друга, с серыми швами. " +
        "Хотя все швы строго горизонтальны, кажется, что они наклонены.";

    public override TransformerVariable[] AvailableCustomVariables { get; } =
        [TilesAcrossVariable, ContrastVariable, RowShiftVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int tilesAcross = (int)Math.Round(customVariables.GetValue(TilesAcrossVariable));
        float strength = (float)customVariables.GetValue(ContrastVariable) / PercentScale;
        int rowShiftPercent = (int)Math.Round(customVariables.GetValue(RowShiftVariable));

        var result = new byte[pixels.Length];
        int tile = Math.Max(MinTileSize, Math.Min(width, height) / tilesAcross);
        int mortar = Math.Max(MinMortarWidth, tile / MortarWidthDivisor);
        int rowShift = tile * rowShiftPercent / PercentScale;

        for (int y = 0; y < height; y++)
        {
            int row = y / tile;
            bool isMortar = y % tile < mortar;
            int shift = (row % RowsPerPattern) * rowShift;

            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;

                if (isMortar)
                {
                    result[i] = result[i + 1] = result[i + 2] = MortarGray;
                }
                else
                {
                    bool dark = ((x + shift) / tile) % TilesPerPeriod == 0;
                    for (int c = 0; c < 3; c++)
                    {
                        float v = pixels[i + c];
                        v = dark ? v * (1 - strength) : v + (BitmapHelper.White - v) * strength;
                        result[i + c] = (byte)v;
                    }
                }
                result[i + 3] = BitmapHelper.Opaque;
            }
        }

        return result;
    }
}
