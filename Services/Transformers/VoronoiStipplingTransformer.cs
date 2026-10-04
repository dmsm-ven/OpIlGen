using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

/// <summary>
/// Вороной / stippling: изображение превращается в тысячи точек, расставленных плотнее в тёмных областях.
/// Стили: чёрные точки на белом, ячейки Вороного со средним цветом, ячейки с контуром.
/// Расстановка детерминирована (фиксированный seed): одинаковые параметры дают одинаковую картинку.
/// </summary>
public sealed class VoronoiStipplingTransformer : PixelTransformerBase
{
    private const string Id = "voronoi_stippling";

    private const double DefaultPoints = 4000;
    private const double DefaultContrast = 1.5;
    private const double DefaultStyle = 0;
    private const double DefaultSizePercent = 70;

    private const int Seed = 20240607;

    /// <summary>Сколько кандидатов рассматривается для каждой новой точки (берётся самый удалённый от соседей).</summary>
    private const int Candidates = 4;

    /// <summary>Сколько раз пробуем выбрать место по плотности, прежде чем взять последнее попавшееся.</summary>
    private const int MaxRejectionTries = 64;

    /// <summary>Минимальная плотность на самых светлых местах (доля от максимальной).</summary>
    private const double MinDensity = 0.02;

    private const double MaxLuminance = 255.0;

    /// <summary>Радиус точки в долях среднего расстояния между точками (при размере 100% на чёрном).</summary>
    private const double DotRadiusFactor = 0.75;

    /// <summary>Минимальный размер точки на самом светлом месте (доля от максимального).</summary>
    private const double MinDotScale = 0.35;

    private const int StyleDots = 0;
    private const int StyleOutlined = 2;

    private const byte OutlineGray = 30;

    private static readonly TransformerVariable PointsVariable =
        TransformerVariableFactory.Create(Id, "points", 300, DefaultPoints, 20000, 100);

    private static readonly TransformerVariable ContrastVariable =
        TransformerVariableFactory.Create(Id, "contrast", 0, DefaultContrast, 4, 0.1);

    private static readonly TransformerVariable StyleVariable =
        TransformerVariableFactory.Create(Id, "style", 0, DefaultStyle, 2, 1);

    private static readonly TransformerVariable SizeVariable =
        TransformerVariableFactory.Create(Id, "size", 10, DefaultSizePercent, 100, 5);

    public VoronoiStipplingTransformer(ILocalizationService localizer) : base(localizer)
    {
    }

    public override string Name => Localizer.Get("transformer.voronoi_stippling.name");
    public override string Key => Id;
    public override string Description => Localizer.Get("transformer.voronoi_stippling.description");

    public override TransformerVariable[] AvailableCustomVariables { get; } =
        [PointsVariable, ContrastVariable, StyleVariable, SizeVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int points = (int)Math.Round(customVariables.GetValue(PointsVariable));
        double gamma = customVariables.GetValue(ContrastVariable);
        int style = (int)Math.Round(customVariables.GetValue(StyleVariable));
        double size = customVariables.GetValue(SizeVariable) / 100.0;

        // Размытая яркость: плотность точек не должна «дёргаться» от мелких деталей
        var lum = BitmapHelper.ToLuminance(pixels);
        BitmapHelper.BoxBlur(lum, width, height, Math.Max(1, Math.Min(width, height) / 200));

        double spacing = Math.Sqrt((double)width * height / points);
        var grid = BuildSites(lum, width, height, points, spacing, gamma);

        return style == StyleDots
            ? RenderDots(grid, lum, width, height, spacing, size)
            : RenderCells(grid, pixels, width, height, style == StyleOutlined);
    }

    /// <summary>Расставляет точки: плотность по яркости, из нескольких кандидатов берётся самый удалённый от соседей.</summary>
    private static SiteGrid BuildSites(float[] lum, int width, int height, int points, double spacing, double gamma)
    {
        var density = BuildDensity(lum, gamma);
        var grid = new SiteGrid(width, height, points, Math.Max(2, spacing));
        var rng = new Random(Seed);

        for (int i = 0; i < points; i++)
        {
            float bestX = 0, bestY = 0;
            double bestScore = -1;

            for (int c = 0; c < Candidates; c++)
            {
                DrawCandidate(rng, density, width, height, out float x, out float y);
                double score = double.MaxValue;
                if (grid.Count > 0)
                {
                    grid.Nearest(x, y, out score);
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = x;
                    bestY = y;
                }
            }

            grid.Add(bestX, bestY);
        }

        return grid;
    }

    /// <summary>Плотность (0..1) в каждом пикселе: тёмное - плотнее. Максимум нормирован на 1.</summary>
    private static float[] BuildDensity(float[] lum, double gamma)
    {
        var density = new float[lum.Length];
        double max = 0;

        for (int p = 0; p < lum.Length; p++)
        {
            double darkness = 1.0 - lum[p] / MaxLuminance;
            double value = Math.Pow(MinDensity + (1 - MinDensity) * darkness, gamma);
            density[p] = (float)value;
            if (value > max)
            {
                max = value;
            }
        }

        if (max > 0)
        {
            for (int p = 0; p < density.Length; p++)
            {
                density[p] = (float)(density[p] / max);
            }
        }

        return density;
    }

    private static void DrawCandidate(Random rng, float[] density, int width, int height, out float x, out float y)
    {
        x = 0;
        y = 0;
        for (int attempt = 0; attempt < MaxRejectionTries; attempt++)
        {
            x = (float)(rng.NextDouble() * width);
            y = (float)(rng.NextDouble() * height);

            int ix = Math.Min(width - 1, (int)x);
            int iy = Math.Min(height - 1, (int)y);
            if (rng.NextDouble() <= density[iy * width + ix])
            {
                return;
            }
        }
    }

    private static byte[] RenderDots(SiteGrid grid, float[] lum, int width, int height, double spacing, double size)
    {
        var radius = new float[grid.Count];
        for (int i = 0; i < grid.Count; i++)
        {
            int ix = Math.Clamp((int)grid.X[i], 0, width - 1);
            int iy = Math.Clamp((int)grid.Y[i], 0, height - 1);
            double darkness = 1.0 - lum[iy * width + ix] / MaxLuminance;
            radius[i] = (float)(DotRadiusFactor * spacing * size * (MinDotScale + (1 - MinDotScale) * darkness));
        }

        var result = new byte[width * height * 4];
        Array.Fill(result, BitmapHelper.White);

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int site = grid.Nearest(x + 0.5, y + 0.5, out double distanceSquared);
                if (site >= 0 && distanceSquared <= (double)radius[site] * radius[site])
                {
                    BitmapHelper.SetGray(result, y * width + x, BitmapHelper.Black);
                }
            }
        });

        return result;
    }

    private static byte[] RenderCells(SiteGrid grid, byte[] pixels, int width, int height, bool outline)
    {
        var ids = new int[width * height];
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                ids[y * width + x] = grid.Nearest(x + 0.5, y + 0.5, out _);
            }
        });

        // Средний цвет каждой ячейки
        var sumB = new long[grid.Count];
        var sumG = new long[grid.Count];
        var sumR = new long[grid.Count];
        var count = new int[grid.Count];
        for (int p = 0; p < ids.Length; p++)
        {
            int id = ids[p];
            int o = p * 4;
            sumB[id] += pixels[o];
            sumG[id] += pixels[o + 1];
            sumR[id] += pixels[o + 2];
            count[id]++;
        }

        var result = new byte[pixels.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int p = y * width + x;
                int id = ids[p];

                bool isBorder = outline
                    && ((x > 0 && ids[p - 1] != id) || (y > 0 && ids[p - width] != id));

                if (isBorder)
                {
                    BitmapHelper.SetGray(result, p, OutlineGray);
                }
                else
                {
                    PixelSampler.SetPixel(
                        result, p,
                        (float)sumB[id] / count[id], (float)sumG[id] / count[id], (float)sumR[id] / count[id]);
                }
            }
        }

        return result;
    }

    /// <summary>Равномерная сетка корзин для быстрого поиска ближайшей точки.</summary>
    private sealed class SiteGrid
    {
        private readonly double _cell;
        private readonly int _cols;
        private readonly int _rows;
        private readonly int[] _head;
        private readonly int[] _next;

        public SiteGrid(int width, int height, int capacity, double cell)
        {
            _cell = cell;
            _cols = (int)Math.Ceiling(width / cell) + 1;
            _rows = (int)Math.Ceiling(height / cell) + 1;
            _head = new int[_cols * _rows];
            Array.Fill(_head, -1);
            _next = new int[capacity];
            X = new float[capacity];
            Y = new float[capacity];
        }

        public float[] X { get; }

        public float[] Y { get; }

        public int Count { get; private set; }

        public void Add(float x, float y)
        {
            int id = Count++;
            X[id] = x;
            Y[id] = y;

            int cell = Math.Clamp((int)(y / _cell), 0, _rows - 1) * _cols + Math.Clamp((int)(x / _cell), 0, _cols - 1);
            _next[id] = _head[cell];
            _head[cell] = id;
        }

        /// <summary>Ближайшая точка к (x, y) и квадрат расстояния до неё. -1, если точек нет.</summary>
        public int Nearest(double x, double y, out double distanceSquared)
        {
            int cx = Math.Clamp((int)(x / _cell), 0, _cols - 1);
            int cy = Math.Clamp((int)(y / _cell), 0, _rows - 1);
            int best = -1;
            double bestSquared = double.MaxValue;
            int maxRing = Math.Max(_cols, _rows);

            for (int ring = 0; ring <= maxRing; ring++)
            {
                int x0 = cx - ring, x1 = cx + ring;
                int y0 = cy - ring, y1 = cy + ring;

                for (int gy = y0; gy <= y1; gy++)
                {
                    if (gy < 0 || gy >= _rows)
                    {
                        continue;
                    }

                    // На кольце: верхняя и нижняя строки целиком, в остальных только крайние ячейки
                    bool edgeRow = gy == y0 || gy == y1;
                    int step = edgeRow ? 1 : x1 - x0;

                    for (int gx = x0; gx <= x1; gx += step)
                    {
                        if (gx < 0 || gx >= _cols)
                        {
                            continue;
                        }

                        for (int s = _head[gy * _cols + gx]; s >= 0; s = _next[s])
                        {
                            double dx = X[s] - x;
                            double dy = Y[s] - y;
                            double squared = dx * dx + dy * dy;
                            if (squared < bestSquared)
                            {
                                bestSquared = squared;
                                best = s;
                            }
                        }
                    }
                }

                // Всё, что дальше внешнего кольца, не ближе ring * cell
                double reach = ring * _cell;
                if (best >= 0 && bestSquared <= reach * reach)
                {
                    break;
                }
            }

            distanceSquared = bestSquared;
            return best;
        }
    }
}
