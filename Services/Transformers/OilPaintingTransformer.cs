using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

/// <summary>
/// Масляная живопись: для каждого пикселя в круглой окрестности (кисть) выбирается самый частый уровень яркости,
/// а цвет мазка - средний цвет пикселей этого уровня. Окно скользит по строке, поэтому при смещении
/// на пиксель обновляются только краевые пиксели; строки обрабатываются параллельно.
/// </summary>
public sealed class OilPaintingTransformer : PixelTransformerBase
{
    /// <summary>Радиус кисти, px.</summary>
    private const int DefaultBrushSize = 3;

    /// <summary>Сколько уровней яркости различается при выборе цвета мазка.</summary>
    private const int DefaultCoarseness = 50;

    private const int MaxIntensity = 255;

    // Яркость пикселя (0..255) целыми числами: (7471 B + 38470 G + 19595 R) / 65536 - те же 0.114 / 0.587 / 0.299
    private const int IntensityBlueWeight = 7471;
    private const int IntensityGreenWeight = 38470;
    private const int IntensityRedWeight = 19595;
    private const int IntensityShift = 16;

    private static readonly TransformerVariable BrushSizeVariable = new()
    {
        NameKey = "transformer.oil_painting.var.brush_size.name",
        Key = "brush_size",
        DescriptionKey = "transformer.oil_painting.var.brush_size.description",
        MinValue = 1,
        DefaultValue = DefaultBrushSize,
        MaxValue = 8,
        Step = 1
    };

    private static readonly TransformerVariable CoarsenessVariable = new()
    {
        NameKey = "transformer.oil_painting.var.coarseness.name",
        Key = "coarseness",
        DescriptionKey = "transformer.oil_painting.var.coarseness.description",
        MinValue = 3,
        DefaultValue = DefaultCoarseness,
        MaxValue = MaxIntensity,
        Step = 1
    };

    public OilPaintingTransformer(ILocalizationService localizer) : base(localizer)
    {
    }

    public override string Name => Localizer.Get("transformer.oil_painting.name");
    public override string Key => "oil_painting";
    public override string Description => Localizer.Get("transformer.oil_painting.description");

    public override TransformerVariable[] AvailableCustomVariables { get; } = [BrushSizeVariable, CoarsenessVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int radius = (int)Math.Round(customVariables.GetValue(BrushSizeVariable));
        int levels = (int)Math.Round(customVariables.GetValue(CoarsenessVariable));

        // Уровень яркости каждого пикселя
        var bins = new byte[width * height];
        for (int p = 0, i = 0; p < bins.Length; p++, i += 4)
        {
            int intensity = (IntensityBlueWeight * pixels[i]
                           + IntensityGreenWeight * pixels[i + 1]
                           + IntensityRedWeight * pixels[i + 2]) >> IntensityShift;
            bins[p] = (byte)(intensity * (levels - 1) / MaxIntensity);
        }

        // Полуширина круглой кисти для каждой строки окна: dx² + dy² <= radius²
        var halfWidths = new int[2 * radius + 1];
        for (int dy = -radius; dy <= radius; dy++)
        {
            halfWidths[dy + radius] = (int)Math.Floor(Math.Sqrt(radius * radius - dy * dy));
        }

        var result = new byte[pixels.Length];

        // Строки независимы: у каждого потока своя гистограмма
        Parallel.For(0, height,
            () => new Histogram(levels),
            (y, _, histogram) =>
            {
                RenderRow(y, pixels, bins, result, width, height, radius, halfWidths, levels, histogram);
                return histogram;
            },
            _ => { });

        return result;
    }

    private static void RenderRow(
        int y, byte[] pixels, byte[] bins, byte[] result,
        int width, int height, int radius, int[] halfWidths, int levels, Histogram histogram)
    {
        histogram.Clear();

        // Окно для первого пикселя строки
        for (int dy = -radius; dy <= radius; dy++)
        {
            int rowY = y + dy;
            if (rowY < 0 || rowY >= height) continue;

            int lastX = Math.Min(halfWidths[dy + radius], width - 1);
            for (int x = 0; x <= lastX; x++)
            {
                histogram.Add(pixels, bins, rowY * width + x);
            }
        }

        for (int x = 0; x < width; x++)
        {
            // Самый частый уровень яркости (при равенстве - самый тёмный) и средний цвет его пикселей
            int best = 0, bestCount = 0;
            for (int level = 0; level < levels; level++)
            {
                if (histogram.Count[level] > bestCount)
                {
                    bestCount = histogram.Count[level];
                    best = level;
                }
            }

            int i = (y * width + x) * 4;
            result[i] = (byte)(histogram.SumB[best] / bestCount);
            result[i + 1] = (byte)(histogram.SumG[best] / bestCount);
            result[i + 2] = (byte)(histogram.SumR[best] / bestCount);
            result[i + 3] = BitmapHelper.Opaque;

            // Сдвиг окна на пиксель вправо: убираем левый край, добавляем правый
            for (int dy = -radius; dy <= radius; dy++)
            {
                int rowY = y + dy;
                if (rowY < 0 || rowY >= height) continue;

                int halfWidth = halfWidths[dy + radius];

                int removeX = x - halfWidth;
                if (removeX >= 0) histogram.Remove(pixels, bins, rowY * width + removeX);

                int addX = x + 1 + halfWidth;
                if (addX < width) histogram.Add(pixels, bins, rowY * width + addX);
            }
        }
    }

    /// <summary>Гистограмма окна: сколько пикселей каждого уровня яркости и сумма их цветов.</summary>
    private sealed class Histogram
    {
        public readonly int[] Count;
        public readonly int[] SumB;
        public readonly int[] SumG;
        public readonly int[] SumR;

        public Histogram(int levels)
        {
            Count = new int[levels];
            SumB = new int[levels];
            SumG = new int[levels];
            SumR = new int[levels];
        }

        public void Clear()
        {
            Array.Clear(Count);
            Array.Clear(SumB);
            Array.Clear(SumG);
            Array.Clear(SumR);
        }

        public void Add(byte[] pixels, byte[] bins, int pixel)
        {
            int level = bins[pixel];
            int i = pixel * 4;
            Count[level]++;
            SumB[level] += pixels[i];
            SumG[level] += pixels[i + 1];
            SumR[level] += pixels[i + 2];
        }

        public void Remove(byte[] pixels, byte[] bins, int pixel)
        {
            int level = bins[pixel];
            int i = pixel * 4;
            Count[level]--;
            SumB[level] -= pixels[i];
            SumG[level] -= pixels[i + 1];
            SumR[level] -= pixels[i + 2];
        }
    }
}
