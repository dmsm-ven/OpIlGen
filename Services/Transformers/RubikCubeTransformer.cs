using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

/// <summary>
/// Кубик Рубика: в центре изображения стоит куб 3x3, а вместо цветных наклеек на нём плитки изображения
/// в «пазловом» виде (перемешанные). Куб рисуется настоящей перспективой: верхняя грань почти не искажена,
/// боковые сильно скошены. Расстояние до камеры, наклон, поворот, степень перемешивания
/// и яркость фона настраиваются.
/// </summary>
public sealed class RubikCubeTransformer : PixelTransformerBase
{
    private const string Id = "rubik_cube";

    private const double DefaultDistance = 6;
    private const double DefaultElevationDegrees = 35;
    private const double DefaultRotationDegrees = 35;
    private const double DefaultScramblePercent = 100;
    private const double DefaultSeed = 1;
    private const double DefaultBackgroundPercent = 35;

    private const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>Фокусное расстояние в долях короткой стороны изображения.</summary>
    private const double FocalFactor = 1.0;

    private static readonly TransformerVariable DistanceVariable =
        TransformerVariableFactory.Create(Id, "distance", 3, DefaultDistance, 20, 0.5);

    private static readonly TransformerVariable ElevationVariable =
        TransformerVariableFactory.Create(Id, "elevation", 0, DefaultElevationDegrees, 90, 5);

    private static readonly TransformerVariable RotationVariable =
        TransformerVariableFactory.Create(Id, "rotation", 0, DefaultRotationDegrees, 360, 5);

    private static readonly TransformerVariable ScrambleVariable =
        TransformerVariableFactory.Create(Id, "scramble", 0, DefaultScramblePercent, 100, 5);

    private static readonly TransformerVariable SeedVariable =
        TransformerVariableFactory.Create(Id, "seed", 0, DefaultSeed, 99, 1);

    private static readonly TransformerVariable BackgroundVariable =
        TransformerVariableFactory.Create(Id, "background", 0, DefaultBackgroundPercent, 100, 5);

    public RubikCubeTransformer(ILocalizationService localizer) : base(localizer)
    {
    }

    public override string Name => Localizer.Get("transformer.rubik_cube.name");
    public override string Key => Id;
    public override string Description => Localizer.Get("transformer.rubik_cube.description");

    public override TransformerVariable[] AvailableCustomVariables { get; } =
    [
        DistanceVariable, ElevationVariable, RotationVariable,
        ScrambleVariable, SeedVariable, BackgroundVariable
    ];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        double distance = customVariables.GetValue(DistanceVariable);
        double elevation = customVariables.GetValue(ElevationVariable) * DegreesToRadians;
        double rotation = customVariables.GetValue(RotationVariable) * DegreesToRadians;
        double scramble = customVariables.GetValue(ScrambleVariable) / 100.0;
        int seed = (int)Math.Round(customVariables.GetValue(SeedVariable));
        float background = (float)(customVariables.GetValue(BackgroundVariable) / 100.0);

        var scene = new CubeScene(pixels, width, height, distance, elevation, rotation, scramble, seed);

        const int samples = CubeScene.Samples;
        const int total = samples * samples;
        var result = new byte[pixels.Length];

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                float cubeBlue = 0, cubeGreen = 0, cubeRed = 0;
                int hits = 0;

                // Сглаживание краёв: несколько лучей на пиксель
                for (int sy = 0; sy < samples; sy++)
                {
                    for (int sx = 0; sx < samples; sx++)
                    {
                        double px = x + (sx + 0.5) / samples - 0.5;
                        double py = y + (sy + 0.5) / samples - 0.5;
                        if (scene.Trace(px, py, out float b, out float g, out float r))
                        {
                            cubeBlue += b;
                            cubeGreen += g;
                            cubeRed += r;
                            hits++;
                        }
                    }
                }

                // Фон - исходный пиксель, приглушённый до заданной яркости
                int p = y * width + x;
                int o = p * 4;
                int missed = total - hits;
                float blue = (cubeBlue + pixels[o] * background * missed) / total;
                float green = (cubeGreen + pixels[o + 1] * background * missed) / total;
                float red = (cubeRed + pixels[o + 2] * background * missed) / total;

                PixelSampler.SetPixel(result, p, blue, green, red);
            }
        });

        return result;
    }

    /// <summary>
    /// Куб с центром на расстоянии <c>distance</c> от камеры (ребро = 2). Для каждого луча ищется пересечение с кубом,
    /// по точке на грани определяется наклейка, а по ней - плитка изображения.
    /// </summary>
    private sealed class CubeScene
    {
        /// <summary>Лучей на пиксель по каждой оси (всего Samples x Samples).</summary>
        public const int Samples = 2;

        private const int Cells = 3;
        private const int FaceCount = 6;
        private const int TileCount = Cells * Cells;

        // Размер наклейки: зазор между ними и скругление углов (в долях ячейки)
        private const double Gap = 0.07;
        private const double CornerRadius = 0.16;
        private const float BodyColor = 20f;

        // Освещение: свет сверху-слева-спереди (в системе камеры)
        private const double Ambient = 0.6;
        private const double Diffuse = 0.4;
        private const double LightX = -0.35;
        private const double LightY = 0.75;
        private const double LightZ = -0.55;

        private const int MinTextureSize = 96;
        private const int MaxTextureSize = 1536;
        private const int MaxTextureSubSamples = 4;

        // Грани: +X, -X, +Y, -Y, +Z, -Z. Правый и верхний векторы грани (взгляд снаружи, без зеркалирования)
        private static readonly double[][] FaceRight =
        [
            [0, 0, -1], [0, 0, 1], [1, 0, 0], [1, 0, 0], [1, 0, 0], [-1, 0, 0]
        ];

        private static readonly double[][] FaceUp =
        [
            [0, 1, 0], [0, 1, 0], [0, 0, -1], [0, 0, 1], [0, 1, 0], [0, 1, 0]
        ];

        private readonly double _focal;
        private readonly double _centerX;
        private readonly double _centerY;
        private readonly double _distance;

        // Матрица вращения куба (строки), координаты: x вправо, y вверх, z от камеры
        private readonly double[] _rotation = new double[9];

        private readonly int[][] _permutations = new int[FaceCount][];
        private readonly double[] _faceShade = new double[FaceCount];
        private readonly float[] _texture;
        private readonly int _textureSize;

        public CubeScene(
            byte[] pixels, int width, int height,
            double distance, double elevation, double rotation, double scramble, int seed)
        {
            int side = Math.Min(width, height);
            _focal = side * FocalFactor;
            _centerX = (width - 1) / 2.0;
            _centerY = (height - 1) / 2.0;
            _distance = distance;

            // R = Rx(-elevation) * Ry(rotation): куб поворачивается вокруг вертикали, затем наклоняется к камере
            double cy = Math.Cos(rotation), sy = Math.Sin(rotation);
            double ce = Math.Cos(elevation), se = Math.Sin(elevation);
            _rotation[0] = cy; _rotation[1] = 0; _rotation[2] = sy;
            _rotation[3] = -se * sy; _rotation[4] = ce; _rotation[5] = se * cy;
            _rotation[6] = -ce * sy; _rotation[7] = -se; _rotation[8] = ce * cy;

            // Затенение граней по нормали в мировых координатах
            double lightLength = Math.Sqrt(LightX * LightX + LightY * LightY + LightZ * LightZ);
            for (int face = 0; face < FaceCount; face++)
            {
                int axis = face / 2;
                double sign = face % 2 == 0 ? 1 : -1;
                double nx = sign * _rotation[axis];
                double ny = sign * _rotation[3 + axis];
                double nz = sign * _rotation[6 + axis];
                double lambert = Math.Max(0, (nx * LightX + ny * LightY + nz * LightZ) / lightLength);
                _faceShade[face] = Ambient + Diffuse * lambert;
            }

            // Перемешивание: у каждой грани своя перестановка плиток. Это первые K шагов
            // тасования Фишера-Йетса, поэтому с ростом степени перемешивания плитки сдвигаются постепенно.
            int swapCount = (int)Math.Round(scramble * (TileCount - 1));
            for (int face = 0; face < FaceCount; face++)
            {
                var random = new Random(unchecked(seed * 7919 + face * 104729));
                var order = new int[TileCount];
                for (int i = 0; i < TileCount; i++)
                {
                    order[i] = i;
                }

                for (int step = 0, i = TileCount - 1; step < swapCount; step++, i--)
                {
                    int j = random.Next(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }

                _permutations[face] = order;
            }

            // Текстура грани: центральный квадрат изображения, заранее уменьшенный до размера куба на экране
            int estimated = (int)Math.Ceiling(2 * _focal / distance * 2);
            _textureSize = Math.Min(side, Math.Clamp(estimated, MinTextureSize, MaxTextureSize));
            _texture = BuildTexture(pixels, width, height, side, _textureSize);
        }

        /// <summary>Пускает луч через точку экрана; возвращает true и цвет, если луч попал в куб.</summary>
        public bool Trace(double px, double py, out float b, out float g, out float r)
        {
            b = g = r = 0;

            double dx = (px - _centerX) / _focal;
            double dy = -(py - _centerY) / _focal;
            double invLength = 1.0 / Math.Sqrt(dx * dx + dy * dy + 1);
            dx *= invLength;
            dy *= invLength;
            double dz = invLength;

            // Луч в системе куба: направление R^T * d, начало R^T * (камера - центр куба)
            Span<double> direction = stackalloc double[3];
            Span<double> origin = stackalloc double[3];
            for (int i = 0; i < 3; i++)
            {
                direction[i] = _rotation[i] * dx + _rotation[3 + i] * dy + _rotation[6 + i] * dz;
                origin[i] = -_rotation[6 + i] * _distance;
            }

            // Пересечение с кубом [-1, 1]^3 (метод плоскостей)
            double near = double.NegativeInfinity;
            double far = double.PositiveInfinity;
            int axis = 0;
            for (int i = 0; i < 3; i++)
            {
                double d = direction[i];
                if (Math.Abs(d) < 1e-12)
                {
                    if (Math.Abs(origin[i]) > 1)
                    {
                        return false;
                    }

                    continue;
                }

                double t1 = (-1 - origin[i]) / d;
                double t2 = (1 - origin[i]) / d;
                double tMin = Math.Min(t1, t2);
                double tMax = Math.Max(t1, t2);
                if (tMin > near)
                {
                    near = tMin;
                    axis = i;
                }

                far = Math.Min(far, tMax);
            }

            if (near <= 0 || near > far)
            {
                return false;
            }

            int face = axis * 2 + (direction[axis] > 0 ? 1 : 0);
            double hx = origin[0] + near * direction[0];
            double hy = origin[1] + near * direction[1];
            double hz = origin[2] + near * direction[2];

            // Координаты на грани (0..1) и наклейка в сетке 3x3
            var right = FaceRight[face];
            var up = FaceUp[face];
            double u = (hx * right[0] + hy * right[1] + hz * right[2]) * 0.5 + 0.5;
            double v = 0.5 - (hx * up[0] + hy * up[1] + hz * up[2]) * 0.5;

            double cellU = Math.Clamp(u * Cells, 0, Cells - 1e-9);
            double cellV = Math.Clamp(v * Cells, 0, Cells - 1e-9);
            int column = (int)cellU;
            int row = (int)cellV;
            double localU = cellU - column;
            double localV = cellV - row;

            float shade = (float)_faceShade[face];

            // Зазор и скруглённые углы: вне наклейки виден чёрный пластик
            if (!InsideSticker(localU, localV))
            {
                b = g = r = BodyColor * shade;
                return true;
            }

            int tile = _permutations[face][row * Cells + column];
            int tileX = tile % Cells;
            int tileY = tile / Cells;

            // Внутренняя часть наклейки целиком показывает свою плитку
            double innerU = Math.Clamp((localU - Gap) / (1 - 2 * Gap), 0, 1);
            double innerV = Math.Clamp((localV - Gap) / (1 - 2 * Gap), 0, 1);
            double textureX = (tileX + innerU) / Cells * _textureSize - 0.5;
            double textureY = (tileY + innerV) / Cells * _textureSize - 0.5;

            SampleTexture(_texture, _textureSize, textureX, textureY, out b, out g, out r);
            b *= shade;
            g *= shade;
            r *= shade;
            return true;
        }

        private static bool InsideSticker(double u, double v)
        {
            double half = 0.5 - Gap - CornerRadius;
            double qx = Math.Abs(u - 0.5) - half;
            double qy = Math.Abs(v - 0.5) - half;
            double outside = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0));
            double distance = outside + Math.Min(Math.Max(qx, qy), 0) - CornerRadius;
            return distance <= 0;
        }

        /// <summary>Центральный квадрат изображения, уменьшенный до size x size усреднением (каналы B, G, R).</summary>
        private static float[] BuildTexture(byte[] pixels, int width, int height, int side, int size)
        {
            double offsetX = (width - side) / 2.0;
            double offsetY = (height - side) / 2.0;
            double step = (double)side / size;
            int sub = Math.Clamp((int)Math.Ceiling(step), 1, MaxTextureSubSamples);

            var texture = new float[size * size * 3];
            Parallel.For(0, size, ty =>
            {
                for (int tx = 0; tx < size; tx++)
                {
                    float sumB = 0, sumG = 0, sumR = 0;
                    for (int j = 0; j < sub; j++)
                    {
                        for (int i = 0; i < sub; i++)
                        {
                            double x = offsetX + (tx + (i + 0.5) / sub) * step - 0.5;
                            double y = offsetY + (ty + (j + 0.5) / sub) * step - 0.5;
                            PixelSampler.SampleBilinear(
                                pixels, width, height, x, y, AddressMode.Clamp,
                                out float b, out float g, out float r);
                            sumB += b;
                            sumG += g;
                            sumR += r;
                        }
                    }

                    float norm = 1f / (sub * sub);
                    int o = (ty * size + tx) * 3;
                    texture[o] = sumB * norm;
                    texture[o + 1] = sumG * norm;
                    texture[o + 2] = sumR * norm;
                }
            });

            return texture;
        }

        private static void SampleTexture(
            float[] texture, int size, double x, double y, out float b, out float g, out float r)
        {
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            float fx = (float)(x - x0);
            float fy = (float)(y - y0);

            int xa = Math.Clamp(x0, 0, size - 1);
            int xb = Math.Clamp(x0 + 1, 0, size - 1);
            int ya = Math.Clamp(y0, 0, size - 1);
            int yb = Math.Clamp(y0 + 1, 0, size - 1);

            int i00 = (ya * size + xa) * 3;
            int i10 = (ya * size + xb) * 3;
            int i01 = (yb * size + xa) * 3;
            int i11 = (yb * size + xb) * 3;

            float w00 = (1 - fx) * (1 - fy);
            float w10 = fx * (1 - fy);
            float w01 = (1 - fx) * fy;
            float w11 = fx * fy;

            b = texture[i00] * w00 + texture[i10] * w10 + texture[i01] * w01 + texture[i11] * w11;
            g = texture[i00 + 1] * w00 + texture[i10 + 1] * w10 + texture[i01 + 1] * w01 + texture[i11 + 1] * w11;
            r = texture[i00 + 2] * w00 + texture[i10 + 2] * w10 + texture[i01 + 2] * w01 + texture[i11 + 2] * w11;
        }
    }
}
