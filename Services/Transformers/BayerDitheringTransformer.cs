namespace OpIlGen.Services.Transformers;

public sealed class BayerDitheringTransformer : PixelTransformerBase
{
    /// <summary>Размер матрицы Байера = 2 в этой степени (3 - матрица 8x8).</summary>
    private const int DefaultMatrixExponent = 3;

    /// <summary>Сдвиг порога по яркости (0 - без сдвига).</summary>
    private const int DefaultBias = 0;

    /// <summary>Порог берётся из середины шага матрицы.</summary>
    private const float ThresholdOffset = 0.5f;

    private const float MaxLuminance = 255f;

    // Построение матрицы Байера: каждый шаг делит её на 4 квадранта
    private const int QuadrantScale = 4;
    private const int TopRightOffset = 2;
    private const int BottomLeftOffset = 3;
    private const int BottomRightOffset = 1;

    private static readonly TransformerVariable MatrixExponentVariable = new()
    {
        Name = "Размер матрицы (степень двойки)",
        Key = "matrix_exponent",
        Description = "Размер матрицы порогов: 1 = 2x2, 2 = 4x4, 3 = 8x8, 4 = 16x16. " +
                      "Малые матрицы дают крупный регулярный узор, большие - более плавные полутона.",
        MinValue = 1,
        DefaultValue = DefaultMatrixExponent,
        MaxValue = 4,
        Step = 1
    };

    private static readonly TransformerVariable BiasVariable = new()
    {
        Name = "Сдвиг порога",
        Key = "bias",
        Description = "Положительное значение делает результат темнее, отрицательное - светлее.",
        MinValue = -100,
        DefaultValue = DefaultBias,
        MaxValue = 100,
        Step = 5
    };

    public override string Name => "Дизеринг Байера (упорядоченный)";
    public override string Key => "dither_bayer";
    public override string Description =>
        "Упорядоченный дизеринг: яркость сравнивается с повторяющейся матрицей порогов. " +
        "Получается характерная регулярная «сетчатая» текстура, в которой тоже видны полутона.";

    public override TransformerVariable[] AvailableCustomVariables { get; } = [MatrixExponentVariable, BiasVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int exponent = (int)Math.Round(customVariables.GetValue(MatrixExponentVariable));
        float bias = (float)customVariables.GetValue(BiasVariable);

        int matrixSize = 1 << exponent;
        var matrix = BuildBayerMatrix(matrixSize);
        int cells = matrixSize * matrixSize;

        var lum = BitmapHelper.ToLuminance(pixels);
        var result = new byte[pixels.Length];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                float threshold = (matrix[y % matrixSize, x % matrixSize] + ThresholdOffset) / cells * MaxLuminance + bias;
                BitmapHelper.SetGray(result, idx, lum[idx] > threshold ? BitmapHelper.White : BitmapHelper.Black);
            }
        }

        return result;
    }

    private static int[,] BuildBayerMatrix(int n)
    {
        var m = new int[1, 1];
        int size = 1;

        while (size < n)
        {
            var next = new int[size * 2, size * 2];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int v = QuadrantScale * m[y, x];
                    next[y, x] = v;
                    next[y, x + size] = v + TopRightOffset;
                    next[y + size, x] = v + BottomLeftOffset;
                    next[y + size, x + size] = v + BottomRightOffset;
                }
            }
            m = next;
            size *= 2;
        }

        return m;
    }
}
