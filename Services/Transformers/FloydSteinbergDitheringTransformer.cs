namespace OpIlGen.Services.Transformers;

public sealed class FloydSteinbergDitheringTransformer : PixelTransformerBase
{
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
                float quantized = old < 128f ? 0f : 255f;
                float error = old - quantized;

                BitmapHelper.SetGray(result, idx, (byte)quantized);

                if (x + 1 < width)
                    lum[idx + 1] += error * 7f / 16f;

                if (y + 1 < height)
                {
                    if (x > 0)
                        lum[idx + width - 1] += error * 3f / 16f;
                    lum[idx + width] += error * 5f / 16f;
                    if (x + 1 < width)
                        lum[idx + width + 1] += error * 1f / 16f;
                }
            }
        }

        return result;
    }
}
