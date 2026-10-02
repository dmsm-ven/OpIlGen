namespace OpIlGen.Services.Transformers;

public sealed class FloydSteinbergDitheringTransformer : PixelTransformerBase
{
    /// <summary>Яркость, начиная с которой пиксель становится белым.</summary>
    private const float Threshold = 128f;

    // Доли ошибки квантования, передаваемые соседним пикселям
    private const float RightWeight = 7f / 16f;
    private const float BottomLeftWeight = 3f / 16f;
    private const float BottomWeight = 5f / 16f;
    private const float BottomRightWeight = 1f / 16f;

    public override string Name => "Дизеринг Флойда-Стейнберга";
    public override string Key => "dither_floyd_steinberg";
    public override string Description =>
        "Только чёрный и белый цвета, но ошибка квантования распределяется по соседним пикселям, " +
        "поэтому глаз «видит» плавные полутона.";

    protected override byte[] Process(byte[] pixels, int width, int height)
    {
        var lum = BitmapHelper.ToLuminance(pixels);
        var result = new byte[pixels.Length];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                float old = lum[idx];
                byte level = old < Threshold ? BitmapHelper.Black : BitmapHelper.White;
                float error = old - level;

                BitmapHelper.SetGray(result, idx, level);

                if (x + 1 < width)
                    lum[idx + 1] += error * RightWeight;

                if (y + 1 < height)
                {
                    if (x > 0)
                        lum[idx + width - 1] += error * BottomLeftWeight;
                    lum[idx + width] += error * BottomWeight;
                    if (x + 1 < width)
                        lum[idx + width + 1] += error * BottomRightWeight;
                }
            }
        }

        return result;
    }
}
