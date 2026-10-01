namespace OpIlGen.Services.Transformers;

public sealed class CafeWallTransformer : PixelTransformerBase
{
    private const float Strength = 0.75f;

    public override string Name => "Кафе-стена";
    public override string Key => "cafe_wall";
    public override string Description =>
        "Ряды светлых и тёмных плиток, сдвинутых друг относительно друга, с серыми швами. " +
        "Хотя все швы строго горизонтальны, кажется, что они наклонены.";

    protected override byte[] Process(byte[] pixels, int width, int height)
    {
        var result = new byte[pixels.Length];
        int tile = Math.Max(16, Math.Min(width, height) / 16);
        int mortar = Math.Max(2, tile / 12);

        for (int y = 0; y < height; y++)
        {
            int row = y / tile;
            bool isMortar = y % tile < mortar;
            int shift = (row % 2) * (tile / 2);

            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;

                if (isMortar)
                {
                    result[i] = result[i + 1] = result[i + 2] = 128;
                }
                else
                {
                    bool dark = ((x + shift) / tile) % 2 == 0;
                    for (int c = 0; c < 3; c++)
                    {
                        float v = pixels[i + c];
                        v = dark ? v * (1 - Strength) : v + (255 - v) * Strength;
                        result[i + c] = (byte)v;
                    }
                }
                result[i + 3] = 255;
            }
        }

        return result;
    }
}
