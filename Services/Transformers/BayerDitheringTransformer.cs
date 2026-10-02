namespace OpIlGen.Services.Transformers;

public sealed class BayerDitheringTransformer : PixelTransformerBase
{
    /// <summary>Сторона матрицы Байера (степень двойки).</summary>
    private const int MatrixSize = 8;

    /// <summary>Порог берётся из середины шага матрицы.</summary>
    private const float ThresholdOffset = 0.5f;

    private const float MaxLuminance = 255f;

    // Построение матрицы Байера: каждый шаг делит её на 4 квадранта
    private const int QuadrantScale = 4;
    private const int TopRightOffset = 2;
    private const int BottomLeftOffset = 3;
    private const int BottomRightOffset = 1;

    private static readonly int[,] Matrix = BuildBayerMatrix(MatrixSize);

    public override string Name => "Дизеринг Байера (8x8)";
    public override string Key => "dither_bayer";
    public override string Description =>
        "Упорядоченный дизеринг: яркость сравнивается с повторяющейся матрицей порогов. " +
        "Получается характерная регулярная «сетчатая» текстура, в которой тоже видны полутона.";

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        var lum = BitmapHelper.ToLuminance(pixels);
        var result = new byte[pixels.Length];
        int cells = MatrixSize * MatrixSize;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                float threshold = (Matrix[y % MatrixSize, x % MatrixSize] + ThresholdOffset) / cells * MaxLuminance;
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
