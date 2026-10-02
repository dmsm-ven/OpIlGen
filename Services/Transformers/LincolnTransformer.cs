namespace OpIlGen.Services.Transformers;

public sealed class LincolnTransformer : PixelTransformerBase
{
    private const int DefaultBlocksOnLongSide = 32;
    private const int MinBlocksOnLongSide = 8;
    private const int MaxBlocksOnLongSide = 128;

    private static readonly TransformerVariable BlocksVariable = new()
    {
        Name = "Блоков по длинной стороне",
        Key = "blocks_on_long_side",
        Description = "Сколько крупных «пикселей» умещается вдоль длинной стороны изображения. " +
                      "Чем меньше значение, тем крупнее блоки и сильнее эффект.",
        MinValue = MinBlocksOnLongSide,
        DefaultValue = DefaultBlocksOnLongSide,
        MaxValue = MaxBlocksOnLongSide,
        Step = 1
    };

    public override string Name => "Эффект Линкольна (пикселизация)";
    public override string Key => "lincoln";
    public override string Description =>
        "Крупная пикселизация. Вблизи изображение нечитаемо, но если отойти подальше, " +
        "уменьшить окно или прищуриться, картинка внезапно проступает.";

    public override TransformerVariable[] AvailableCustomVariables { get; } = [BlocksVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        int blocksOnLongSide = (int)Math.Round(customVariables.GetValue(BlocksVariable));

        var result = new byte[pixels.Length];
        int block = Math.Max(1, Math.Max(width, height) / blocksOnLongSide);

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
