namespace OpIlGen.Services.Transformers;

public sealed class BayerDitheringTransformer : PixelTransformerBase
{
    private const int MatrixSize = 8;
    private static readonly int[,] Matrix = BuildBayerMatrix(MatrixSize);

    public override string Name => "Дизеринг Байера (8x8)";
    public override string Key => "dither_bayer";
    public override string Description =>
        "Упорядоченный дизеринг: яркость сравнивается с повторяющейся матрицей порогов. " +
        "Получается характерная регулярная «сетчатая» текстура, в которой тоже видны полутона.";

    protected override byte[] Process(byte[] pixels, int width, int height)
    {
        var lum = BitmapHelper.ToLuminance(pixels);
        var result = new byte[pixels.Length];
        int cells = MatrixSize * MatrixSize;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                float threshold = (Matrix[y % MatrixSize, x % MatrixSize] + 0.5f) / cells * 255f;
                BitmapHelper.SetGray(result, idx, lum[idx] > threshold ? (byte)255 : (byte)0);
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
                    int v = 4 * m[y, x];
                    next[y, x] = v;
                    next[y, x + size] = v + 2;
                    next[y + size, x] = v + 3;
                    next[y + size, x + size] = v + 1;
                }
            }
            m = next;
            size *= 2;
        }

        return m;
    }
}
