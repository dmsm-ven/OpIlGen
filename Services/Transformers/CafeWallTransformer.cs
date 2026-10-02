namespace OpIlGen.Services.Transformers;

public sealed class CafeWallTransformer : PixelTransformerBase
{
    /// <summary>Насколько сильно плитки затемняют/осветляют изображение (0..1).</summary>
    private const float Strength = 0.75f;

    /// <summary>Минимальный размер плитки, px.</summary>
    private const int MinTileSize = 16;

    /// <summary>Размер плитки = меньшая сторона изображения / это значение.</summary>
    private const int TileSizeDivisor = 16;

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

    /// <summary>Нечётные ряды сдвигаются на плитку / это значение (на половину плитки).</summary>
    private const int RowShiftDivisor = 2;

    public override string Name => "Кафе-стена";
    public override string Key => "cafe_wall";
    public override string Description =>
        "Ряды светлых и тёмных плиток, сдвинутых друг относительно друга, с серыми швами. " +
        "Хотя все швы строго горизонтальны, кажется, что они наклонены.";

    protected override byte[] Process(byte[] pixels, int width, int height)
    {
        var result = new byte[pixels.Length];
        int tile = Math.Max(MinTileSize, Math.Min(width, height) / TileSizeDivisor);
        int mortar = Math.Max(MinMortarWidth, tile / MortarWidthDivisor);

        for (int y = 0; y < height; y++)
        {
            int row = y / tile;
            bool isMortar = y % tile < mortar;
            int shift = (row % RowsPerPattern) * (tile / RowShiftDivisor);

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
                        v = dark ? v * (1 - Strength) : v + (BitmapHelper.White - v) * Strength;
                        result[i + c] = (byte)v;
                    }
                }
                result[i + 3] = BitmapHelper.Opaque;
            }
        }

        return result;
    }
}
