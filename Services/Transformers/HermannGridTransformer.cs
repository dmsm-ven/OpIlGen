namespace OpIlGen.Services.Transformers;

public sealed class HermannGridTransformer : PixelTransformerBase
{
    private const float Darkening = 0.25f;

    public override string Name => "Сетка Германа";
    public override string Key => "hermann_grid";
    public override string Description =>
        "Затемнённое изображение с белой решёткой. На пересечениях белых линий, куда вы не смотрите " +
        "прямо, появляются призрачные тёмные пятна.";

    protected override byte[] Process(byte[] pixels, int width, int height)
    {
        var result = new byte[pixels.Length];
        int cell = Math.Max(24, Math.Min(width, height) / 10);
        int line = Math.Max(6, cell / 4);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                bool onLine = x % cell < line || y % cell < line;

                if (onLine)
                {
                    result[i] = result[i + 1] = result[i + 2] = 255;
                }
                else
                {
                    result[i] = (byte)(pixels[i] * Darkening);
                    result[i + 1] = (byte)(pixels[i + 1] * Darkening);
                    result[i + 2] = (byte)(pixels[i + 2] * Darkening);
                }
                result[i + 3] = 255;
            }
        }

        return result;
    }
}
