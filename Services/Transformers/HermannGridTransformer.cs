namespace OpIlGen.Services.Transformers;

public sealed class HermannGridTransformer : PixelTransformerBase
{
    /// <summary>Множитель яркости фона (0 - чёрный, 1 - без изменений).</summary>
    private const float Darkening = 0.25f;

    /// <summary>Минимальный размер ячейки сетки, px.</summary>
    private const int MinCellSize = 24;

    /// <summary>Размер ячейки = меньшая сторона изображения / это значение.</summary>
    private const int CellSizeDivisor = 10;

    /// <summary>Минимальная толщина белой линии, px.</summary>
    private const int MinLineWidth = 6;

    /// <summary>Толщина линии = размер ячейки / это значение.</summary>
    private const int LineWidthDivisor = 4;

    public override string Name => "Сетка Германа";
    public override string Key => "hermann_grid";
    public override string Description =>
        "Затемнённое изображение с белой решёткой. На пересечениях белых линий, куда вы не смотрите " +
        "прямо, появляются призрачные тёмные пятна.";

    protected override byte[] Process(byte[] pixels, int width, int height)
    {
        var result = new byte[pixels.Length];
        int cell = Math.Max(MinCellSize, Math.Min(width, height) / CellSizeDivisor);
        int line = Math.Max(MinLineWidth, cell / LineWidthDivisor);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                bool onLine = x % cell < line || y % cell < line;

                if (onLine)
                {
                    result[i] = result[i + 1] = result[i + 2] = BitmapHelper.White;
                }
                else
                {
                    result[i] = (byte)(pixels[i] * Darkening);
                    result[i + 1] = (byte)(pixels[i + 1] * Darkening);
                    result[i + 2] = (byte)(pixels[i + 2] * Darkening);
                }
                result[i + 3] = BitmapHelper.Opaque;
            }
        }

        return result;
    }
}
