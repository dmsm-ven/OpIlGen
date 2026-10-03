using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

public sealed class HermannGridTransformer : PixelTransformerBase
{
    /// <summary>Сколько ячеек сетки умещается вдоль меньшей стороны изображения.</summary>
    private const int DefaultCellsAcross = 10;

    /// <summary>Толщина белой линии в процентах от размера ячейки.</summary>
    private const int DefaultLineWidthPercent = 25;

    /// <summary>Яркость фона (затемнённого изображения) в процентах от исходной.</summary>
    private const int DefaultBackgroundBrightnessPercent = 25;

    private const int PercentScale = 100;

    /// <summary>Минимальный размер ячейки сетки, px.</summary>
    private const int MinCellSize = 24;

    /// <summary>Минимальная толщина белой линии, px.</summary>
    private const int MinLineWidth = 6;

    private static readonly TransformerVariable CellsAcrossVariable = new()
    {
        NameKey = "transformer.hermann_grid.var.cells_across.name",
        Key = "cells_across",
        DescriptionKey = "transformer.hermann_grid.var.cells_across.description",
        MinValue = 4,
        DefaultValue = DefaultCellsAcross,
        MaxValue = 30,
        Step = 1
    };

    private static readonly TransformerVariable LineWidthVariable = new()
    {
        NameKey = "transformer.hermann_grid.var.line_width_percent.name",
        Key = "line_width_percent",
        DescriptionKey = "transformer.hermann_grid.var.line_width_percent.description",
        MinValue = 5,
        DefaultValue = DefaultLineWidthPercent,
        MaxValue = 50,
        Step = 5
    };

    private static readonly TransformerVariable BackgroundBrightnessVariable = new()
    {
        NameKey = "transformer.hermann_grid.var.background_brightness_percent.name",
        Key = "background_brightness_percent",
        DescriptionKey = "transformer.hermann_grid.var.background_brightness_percent.description",
        MinValue = 0,
        DefaultValue = DefaultBackgroundBrightnessPercent,
        MaxValue = 100,
        Step = 5
    };

    public HermannGridTransformer(ILocalizationService localizer) : base(localizer)
    {
    }

    public override string Name => Localizer.Get("transformer.hermann_grid.name");
    public override string Key => "hermann_grid";
    public override string Description => Localizer.Get("transformer.hermann_grid.description");

    public override TransformerVariable[] AvailableCustomVariables { get; } =
        [CellsAcrossVariable, LineWidthVariable, BackgroundBrightnessVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int cellsAcross = (int)Math.Round(customVariables.GetValue(CellsAcrossVariable));
        int lineWidthPercent = (int)Math.Round(customVariables.GetValue(LineWidthVariable));
        float brightness = (float)customVariables.GetValue(BackgroundBrightnessVariable) / PercentScale;

        var result = new byte[pixels.Length];
        int cell = Math.Max(MinCellSize, Math.Min(width, height) / cellsAcross);
        int line = Math.Max(MinLineWidth, cell * lineWidthPercent / PercentScale);

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
                    result[i] = (byte)(pixels[i] * brightness);
                    result[i + 1] = (byte)(pixels[i + 1] * brightness);
                    result[i + 2] = (byte)(pixels[i + 2] * brightness);
                }
                result[i + 3] = BitmapHelper.Opaque;
            }
        }

        return result;
    }
}
