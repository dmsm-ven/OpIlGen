namespace OpIlGen.Services.Transformers;

public sealed class LincolnTransformer : PixelTransformerBase
{
    private const int BlocksOnLongSide = 32;

    public override string Name => "Эффект Линкольна (пикселизация)";
    public override string Key => "lincoln";
    public override string Description =>
        "Крупная пикселизация. Вблизи изображение нечитаемо, но если отойти подальше, " +
        "уменьшить окно или прищуриться, картинка внезапно проступает.";

    protected override byte[] Process(byte[] pixels, int width, int height)
    {
        var result = new byte[pixels.Length];
        int block = Math.Max(1, Math.Max(width, height) / BlocksOnLongSide);

        for (int by = 0; by < height; by += block)
        {
            for (int bx = 0; bx < width; bx += block)
            {
                int yEnd = Math.Min(by + block, height);
                int xEnd = Math.Min(bx + block, width);
                long sumB = 0, sumG = 0, sumR = 0;
                int count = 0;

                for (int y = by; y < yEnd; y++)
                {
                    for (int x = bx; x < xEnd; x++)
                    {
                        int i = (y * width + x) * 4;
                        sumB += pixels[i];
                        sumG += pixels[i + 1];
                        sumR += pixels[i + 2];
                        count++;
                    }
                }

                byte b = (byte)(sumB / count), g = (byte)(sumG / count), r = (byte)(sumR / count);

                for (int y = by; y < yEnd; y++)
                {
                    for (int x = bx; x < xEnd; x++)
                    {
                        int i = (y * width + x) * 4;
                        result[i] = b;
                        result[i + 1] = g;
                        result[i + 2] = r;
                        result[i + 3] = BitmapHelper.Opaque;
                    }
                }
            }
        }

        return result;
    }
}
