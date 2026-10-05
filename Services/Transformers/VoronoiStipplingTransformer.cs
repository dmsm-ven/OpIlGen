using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

/// <summary>
/// Вороной / stippling: изображение превращается в тысячи точек, расставленных плотнее в тёмных областях.
/// Стили: чёрные точки, цветные ячейки Вороного, ячейки с контуром, круги (контур 1 px) и цветные точки.
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
    private const int StyleCells = 1;
    private const int StyleOutlined = 2;
    private const int StyleCircles = 3;
    private const int StyleColoredDots = 4;

    /// <summary>Толщина линии круга в пикселях (минимальная).</summary>
    private const double RingThicknessPx = 1.0;

    private const byte OutlineGray = 30;

    private static readonly TransformerVariable PointsVariable =
        TransformerVariableFactory.Create(Id, "points", 300, DefaultPoints, 20000, 100);

    private static readonly TransformerVariable ContrastVariable =
        TransformerVariableFactory.Create(Id, "contrast", 0, DefaultContrast, 4, 0.1);

    private static readonly TransformerVariable StyleVariable =
        TransformerVariableFactory.Create(Id, "style", 0, DefaultStyle, 4, 1);

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

        switch (style)
        {
            case StyleCells:
            case StyleOutlined:
                return RenderCells(grid, pixels, width, height, style == StyleOutlined);
            case StyleCircles:
                return RenderCircles(grid, BuildRadii(grid, lum, width, height, spacing, size), width, height);
            case StyleColoredDots:
                return RenderColoredDots(grid, pixels, BuildRadii(grid, lum, width, height, spacing, size), width, height);
            default:
                return RenderDots(grid, BuildRadii(grid, lum, width, height, spacing, size), width, height);
        }
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

    /// <summary>Радиус каждой точки: тёмнее - крупнее (в долях среднего расстояния между точками).</summary>
    private static float[] BuildRadii(SiteGrid grid, float[] lum, int width, int height, double spacing, double size)
    {
        var radius = new float[grid.Count];
        for (int i = 0; i < grid.Count; i++)
        {
            int ix = Math.Clamp((int)grid.X[i], 0, width - 1);
            int iy = Math.Clamp((int)grid.Y[i], 0, height - 1);
            double darkness = 1.0 - lum[iy * width + ix] / MaxLuminance;
            radius[i] = (float)(DotRadiusFactor * spacing * size * (MinDotScale + (1 - MinDotScale) * darkness));
        }

        return radius;
    }

    private static byte[] RenderDots(SiteGrid grid, float[] radius, int width, int height)
    {
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

    /// <summary>Круги: только линия толщиной 1 px, без заливки. Круги целые и могут перекрываться.</summary>
    private static byte[] RenderCircles(SiteGrid grid, float[] radius, int width, int height)
    {
        var result = new byte[width * height * 4];
        Array.Fill(result, BitmapHelper.White);

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                double coverage = grid.RingCoverage(x + 0.5, y + 0.5, radius, RingThicknessPx);
                if (coverage > 0)
                {
                    BitmapHelper.SetGray(result, y * width + x, (byte)Math.Round(MaxLuminance * (1 - coverage)));
                }
            }
        });

        return result;
    }

    /// <summary>Как «точки», но каждая точка закрашена средним цветом своей ячейки.</summary>
    private static byte[] RenderColoredDots(SiteGrid grid, byte[] pixels, float[] radius, int width, int height)
    {
        var cells = AnalyzeCells(grid, pixels, width, height);

        var result = new byte[pixels.Length];
        Array.Fill(result, BitmapHelper.White);

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int p = y * width + x;
                int id = cells.Ids[p];
                double dx = x + 0.5 - grid.X[id];
                double dy = y + 0.5 - grid.Y[id];
                if (dx * dx + dy * dy <= (double)radius[id] * radius[id])
                {
                    PixelSampler.SetPixel(result, p, cells.Blue[id], cells.Green[id], cells.Red[id]);
                }
            }
        });

        return result;
    }

    /// <summary>Для каждого пикселя - номер ближайшей точки, для каждой точки - средний цвет её ячейки.</summary>
    private static (int[] Ids, float[] Blue, float[] Green, float[] Red) AnalyzeCells(
        SiteGrid grid, byte[] pixels, int width, int height)
    {
        var ids = new int[width * height];
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                ids[y * width + x] = grid.Nearest(x + 0.5, y + 0.5, out _);
            }
        });

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

        var blue = new float[grid.Count];
        var green = new float[grid.Count];
        var red = new float[grid.Count];
        for (int i = 0; i < grid.Count; i++)
        {
            // У точки может не оказаться ни одного пикселя (очень плотные точки): цвет тогда не важен
            int n = Math.Max(1, count[i]);
            blue[i] = (float)sumB[i] / n;
            green[i] = (float)sumG[i] / n;
            red[i] = (float)sumR[i] / n;
        }

        return (ids, blue, green, red);
    }

    private static byte[] RenderCells(SiteGrid grid, byte[] pixels, int width, int height, bool outline)
    {
        var cells = AnalyzeCells(grid, pixels, width, height);
        var ids = cells.Ids;

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
                    PixelSampler.SetPixel(result, p, cells.Blue[id], cells.Green[id], cells.Red[id]);
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

        /// <summary>
        /// Покрытие пикселя линиями кругов (0..1): максимум по всем точкам рядом. Радиусы кругов не превышают
        /// размер корзины, поэтому достаточно соседних 3x3 корзин.
        /// </summary>
        public double RingCoverage(double x, double y, float[] radius, double thickness)
        {
            int cx = Math.Clamp((int)(x / _cell), 0, _cols - 1);
            int cy = Math.Clamp((int)(y / _cell), 0, _rows - 1);
            double best = 0;

            for (int gy = Math.Max(0, cy - 1); gy <= Math.Min(_rows - 1, cy + 1); gy++)
            {
                for (int gx = Math.Max(0, cx - 1); gx <= Math.Min(_cols - 1, cx + 1); gx++)
                {
                    for (int s = _head[gy * _cols + gx]; s >= 0; s = _next[s])
                    {
                        double dx = X[s] - x;
                        double dy = Y[s] - y;
                        double distanceToLine = Math.Abs(Math.Sqrt(dx * dx + dy * dy) - radius[s]);
                        double coverage = thickness / 2 + 0.5 - distanceToLine;
                        if (coverage > best)
                        {
                            best = coverage;
                        }
                    }
                }
            }

            return Math.Min(best, 1.0);
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
