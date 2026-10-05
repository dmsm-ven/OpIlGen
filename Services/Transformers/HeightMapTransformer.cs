using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

/// <summary>
/// Карта высот: изображение разбивается на ячейки (квадраты, соты или звёзды с ромбами). Для каждой ячейки
/// считается «высота» по самому изображению: средняя яркость или отличие от соседних ячеек. Высокие ячейки
/// «поднимаются» (полупрозрачная тёплая заливка), низкие «опускаются» (холодная), остальные остаются обычными.
/// </summary>
public sealed class HeightMapTransformer : PixelTransformerBase
{
    private const string Id = "height_map";

    private const double DefaultShape = 1;
    private const double DefaultSize = 20;
    private const double DefaultSource = 0;
    private const double DefaultRaiseThreshold = 65;
    private const double DefaultLowerThreshold = 35;
    private const double DefaultOpacityPercent = 55;
    private const double DefaultInvert = 0;

    private const int ShapeSquare = 0;
    private const int ShapeHexagon = 1;

    private const int SourceNeighbors = 1;

    private const double MaxLuminance = 255.0;

    /// <summary>Высота (0..100) ячейки без отличий от соседей.</summary>
    private const double NeutralHeight = 50;

    /// <summary>Во сколько раз усиливается отличие от соседей: на 1/3 диапазона ярче окружения - предельная высота.</summary>
    private const double NeighborGain = 3;

    /// <summary>Глубина выемки звезды (расстояние внутренней вершины от центра) в долях шага решётки.</summary>
    private const double StarInnerRatio = 0.12;

    private const float GridLineStrength = 0.35f;

    // Цвета заливки (B, G, R): поднятые - тёплый оранжевый, опущенные - холодный синий
    private static readonly (float B, float G, float R) RaisedColor = (0f, 140f, 255f);
    private static readonly (float B, float G, float R) LoweredColor = (235f, 110f, 40f);

    private static readonly TransformerVariable ShapeVariable =
        TransformerVariableFactory.Create(Id, "shape", 0, DefaultShape, 2, 1);

    private static readonly TransformerVariable SizeVariable =
        TransformerVariableFactory.Create(Id, "size", 6, DefaultSize, 100, 1);

    private static readonly TransformerVariable SourceVariable =
        TransformerVariableFactory.Create(Id, "source", 0, DefaultSource, 1, 1);

    private static readonly TransformerVariable RaiseVariable =
        TransformerVariableFactory.Create(Id, "raise_threshold", 0, DefaultRaiseThreshold, 100, 1);

    private static readonly TransformerVariable LowerVariable =
        TransformerVariableFactory.Create(Id, "lower_threshold", 0, DefaultLowerThreshold, 100, 1);

    private static readonly TransformerVariable OpacityVariable =
        TransformerVariableFactory.Create(Id, "opacity", 0, DefaultOpacityPercent, 100, 5);

    private static readonly TransformerVariable InvertVariable =
        TransformerVariableFactory.Create(Id, "invert", 0, DefaultInvert, 1, 1);

    public HeightMapTransformer(ILocalizationService localizer) : base(localizer)
    {
    }

    public override string Name => Localizer.Get("transformer.height_map.name");
    public override string Key => Id;
    public override string Description => Localizer.Get("transformer.height_map.description");

    public override TransformerVariable[] AvailableCustomVariables { get; } =
    [
        ShapeVariable, SizeVariable, SourceVariable, RaiseVariable, LowerVariable, OpacityVariable, InvertVariable
    ];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int shape = (int)Math.Round(customVariables.GetValue(ShapeVariable));
        int size = (int)Math.Round(customVariables.GetValue(SizeVariable));
        bool byNeighbors = (int)Math.Round(customVariables.GetValue(SourceVariable)) == SourceNeighbors;
        double raise = customVariables.GetValue(RaiseVariable);
        double lower = customVariables.GetValue(LowerVariable);
        float opacity = (float)(customVariables.GetValue(OpacityVariable) / 100.0);
        bool invert = Math.Round(customVariables.GetValue(InvertVariable)) >= 1;

        // Нижний порог не может быть выше верхнего
        if (lower > raise)
        {
            (lower, raise) = (raise, lower);
        }

        var lum = BitmapHelper.ToLuminance(pixels);
        var (ids, cellCount) = BuildCells(shape, size, width, height);

        var meanLuminance = MeanPerCell(lum, ids, cellCount);
        double[]? neighborMean = byNeighbors ? NeighborMean(ids, meanLuminance, width, height) : null;

        // Состояние ячейки: -1 опущена, 0 обычная, +1 поднята
        var state = new sbyte[cellCount];
        for (int c = 0; c < cellCount; c++)
        {
            if (double.IsNaN(meanLuminance[c]))
            {
                continue;
            }

            double cellHeight = byNeighbors
                ? NeutralHeight + (meanLuminance[c] - neighborMean![c]) / MaxLuminance * 100.0 * NeighborGain
                : meanLuminance[c] / MaxLuminance * 100.0;
            cellHeight = Math.Clamp(cellHeight, 0, 100);

            if (invert)
            {
                cellHeight = 100 - cellHeight;
            }

            state[c] = (sbyte)(cellHeight >= raise ? 1 : cellHeight <= lower ? -1 : 0);
        }

        var result = new byte[pixels.Length];
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int p = y * width + x;
                int id = ids[p];
                int o = p * 4;
                float b = pixels[o], g = pixels[o + 1], r = pixels[o + 2];

                // Полупрозрачная заливка: изображение под ней просвечивает
                if (state[id] != 0)
                {
                    var color = state[id] > 0 ? RaisedColor : LoweredColor;
                    b += (color.B - b) * opacity;
                    g += (color.G - g) * opacity;
                    r += (color.R - r) * opacity;
                }

                // Тонкая линия по границе ячеек (1 px)
                if ((x > 0 && ids[p - 1] != id) || (y > 0 && ids[p - width] != id))
                {
                    float keep = 1f - GridLineStrength;
                    b *= keep;
                    g *= keep;
                    r *= keep;
                }

                PixelSampler.SetPixel(result, p, b, g, r);
            }
        });

        return result;
    }

    /// <summary>Номер ячейки для каждого пикселя и общее число номеров.</summary>
    private static (int[] Ids, int Count) BuildCells(int shape, int size, int width, int height)
    {
        var ids = new int[width * height];

        if (shape == ShapeSquare)
        {
            int cols = (width + size - 1) / size;
            int rows = (height + size - 1) / size;
            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    ids[y * width + x] = y / size * cols + x / size;
                }
            });
            return (ids, cols * rows);
        }

        if (shape == ShapeHexagon)
        {
            // Соты с вершиной вверх: size - расстояние между параллельными сторонами
            double hexWidth = size;
            double circumradius = size / Math.Sqrt(3);
            double rowHeight = 1.5 * circumradius;
            int cols = (int)Math.Ceiling(width / hexWidth) + 2;
            int rows = (int)Math.Ceiling(height / rowHeight) + 2;

            Parallel.For(0, height, y =>
            {
                double py = y + 0.5;
                int firstRow = (int)Math.Floor(py / rowHeight);
                for (int x = 0; x < width; x++)
                {
                    double px = x + 0.5;
                    int bestId = 0;
                    double best = double.MaxValue;

                    // Ближайший центр соты среди двух соседних рядов (в каждом ряду - ближайший по горизонтали)
                    for (int row = firstRow; row <= firstRow + 1; row++)
                    {
                        double offset = (row & 1) == 1 ? hexWidth / 2 : 0;
                        int col = (int)Math.Floor((px - offset) / hexWidth + 0.5);
                        double dx = px - (col * hexWidth + offset);
                        double dy = py - row * rowHeight;
                        double distance = dx * dx + dy * dy;
                        if (distance < best)
                        {
                            best = distance;
                            bestId = row * cols + col;
                        }
                    }

                    ids[y * width + x] = bestId;
                }
            });
            return (ids, cols * rows);
        }

        return BuildStarCells(ids, size, width, height);
    }

    /// <summary>
    /// Звёзды и ромбы: четырёхконечные звёзды стоят в узлах квадратной решётки (шаг size, острия соприкасаются),
    /// а между ними остаются ромбовидные ячейки. Вместе они покрывают плоскость без щелей.
    /// </summary>
    private static (int[] Ids, int Count) BuildStarCells(int[] ids, int size, int width, int height)
    {
        double step = size;
        double inner = StarInnerRatio * step;
        int cols = (int)Math.Ceiling(width / step) + 3;
        int rows = (int)Math.Ceiling(height / step) + 3;
        int family = cols * rows;

        Parallel.For(0, height, y =>
        {
            double py = y + 0.5;
            int starRow = (int)Math.Floor(py / step + 0.5);
            double dy = py - starRow * step;

            for (int x = 0; x < width; x++)
            {
                double px = x + 0.5;
                int starCol = (int)Math.Floor(px / step + 0.5);
                double dx = px - starCol * step;

                // В пределах одной восьмой квадрата граница звезды - отрезок от острия (step/2, 0) до (inner, inner)
                double u = Math.Abs(dx), v = Math.Abs(dy);
                if (u < v)
                {
                    (u, v) = (v, u);
                }

                bool insideStar = inner * (u - step / 2) + (step / 2 - inner) * v <= 0;
                if (insideStar)
                {
                    ids[y * width + x] = (starRow + 1) * cols + (starCol + 1);
                }
                else
                {
                    // Ромб - ячейка «вторых» узлов решётки, сдвинутых на пол-шага по диагонали от звезды
                    int rhombusCol = dx >= 0 ? starCol : starCol - 1;
                    int rhombusRow = dy >= 0 ? starRow : starRow - 1;
                    ids[y * width + x] = family + (rhombusRow + 1) * cols + (rhombusCol + 1);
                }
            }
        });

        return (ids, family * 2);
    }

    /// <summary>Средняя яркость каждой ячейки (NaN, если в ячейке нет пикселей).</summary>
    private static double[] MeanPerCell(float[] lum, int[] ids, int cellCount)
    {
        var sum = new double[cellCount];
        var count = new int[cellCount];
        for (int p = 0; p < ids.Length; p++)
        {
            sum[ids[p]] += lum[p];
            count[ids[p]]++;
        }

        var mean = new double[cellCount];
        for (int c = 0; c < cellCount; c++)
        {
            mean[c] = count[c] > 0 ? sum[c] / count[c] : double.NaN;
        }

        return mean;
    }

    /// <summary>Средняя яркость соседних ячеек (граничащих хотя бы одним пикселем). Без соседей - своя яркость.</summary>
    private static double[] NeighborMean(int[] ids, double[] mean, int width, int height)
    {
        var pairs = new HashSet<long>();
        long total = mean.Length;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int p = y * width + x;
                int id = ids[p];

                if (x > 0 && ids[p - 1] != id)
                {
                    pairs.Add(PairKey(id, ids[p - 1], total));
                }

                if (y > 0 && ids[p - width] != id)
                {
                    pairs.Add(PairKey(id, ids[p - width], total));
                }
            }
        }

        var sum = new double[mean.Length];
        var count = new int[mean.Length];
        foreach (long key in pairs)
        {
            int a = (int)(key / total);
            int b = (int)(key % total);
            if (double.IsNaN(mean[a]) || double.IsNaN(mean[b]))
            {
                continue;
            }

            sum[a] += mean[b];
            count[a]++;
            sum[b] += mean[a];
            count[b]++;
        }

        var result = new double[mean.Length];
        for (int c = 0; c < mean.Length; c++)
        {
            result[c] = count[c] > 0 ? sum[c] / count[c] : mean[c];
        }

        return result;
    }

    private static long PairKey(int a, int b, long total) => a < b ? a * total + b : b * total + a;
}
