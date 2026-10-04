using OpIlGen.Localization;

namespace OpIlGen.Services.Transformers;

/// <summary>
/// Сортировка пикселей: в каждой строке (или столбце) непрерывные участки, где яркость лежит между нижним
/// и верхним порогом, сортируются по выбранному признаку: яркость, оттенок, насыщенность или красный канал.
/// </summary>
public sealed class PixelSortingTransformer : PixelTransformerBase
{
    private const string Id = "pixel_sorting";

    private const double DefaultDirection = 0;
    private const double DefaultKey = 0;
    private const double DefaultThresholdLow = 50;
    private const double DefaultThresholdHigh = 210;

    private const int KeyLuminance = 0;
    private const int KeyHue = 1;
    private const int KeySaturation = 2;

    private static readonly TransformerVariable DirectionVariable =
        TransformerVariableFactory.Create(Id, "direction", 0, DefaultDirection, 1, 1);

    private static readonly TransformerVariable KeyVariable =
        TransformerVariableFactory.Create(Id, "key", 0, DefaultKey, 3, 1);

    private static readonly TransformerVariable ThresholdLowVariable =
        TransformerVariableFactory.Create(Id, "threshold_low", 0, DefaultThresholdLow, 255, 5);

    private static readonly TransformerVariable ThresholdHighVariable =
        TransformerVariableFactory.Create(Id, "threshold_high", 0, DefaultThresholdHigh, 255, 5);

    public PixelSortingTransformer(ILocalizationService localizer) : base(localizer)
    {
    }

    public override string Name => Localizer.Get("transformer.pixel_sorting.name");
    public override string Key => Id;
    public override string Description => Localizer.Get("transformer.pixel_sorting.description");

    public override TransformerVariable[] AvailableCustomVariables { get; } =
        [DirectionVariable, KeyVariable, ThresholdLowVariable, ThresholdHighVariable];

    protected override byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables)
    {
        bool columns = Math.Round(customVariables.GetValue(DirectionVariable)) >= 1;
        int key = (int)Math.Round(customVariables.GetValue(KeyVariable));
        float low = (float)customVariables.GetValue(ThresholdLowVariable);
        float high = (float)customVariables.GetValue(ThresholdHighVariable);
        if (low > high)
        {
            (low, high) = (high, low);
        }

        var lum = BitmapHelper.ToLuminance(pixels);
        var result = (byte[])pixels.Clone();

        int lineCount = columns ? width : height;
        int lineLength = columns ? height : width;

        Parallel.For(
            0,
            lineCount,
            () => (Keys: new float[lineLength], Values: new uint[lineLength]),
            (line, _, buffers) =>
            {
                SortLine(pixels, result, lum, line, lineLength, width, columns, key, low, high, buffers.Keys, buffers.Values);
                return buffers;
            },
            _ => { });

        return result;
    }

    private static void SortLine(
        byte[] source, byte[] destination, float[] lum, int line, int length, int width,
        bool columns, int key, float low, float high, float[] keys, uint[] values)
    {
        int i = 0;
        while (i < length)
        {
            if (!InRange(lum[Index(line, i, width, columns)], low, high))
            {
                i++;
                continue;
            }

            int start = i;
            int count = 0;
            while (i < length)
            {
                int index = Index(line, i, width, columns);
                if (!InRange(lum[index], low, high))
                {
                    break;
                }

                keys[count] = GetKey(source, index, lum[index], key);
                values[count] = Pack(source, index);
                count++;
                i++;
            }

            if (count > 1)
            {
                Array.Sort(keys, values, 0, count);
            }

            for (int k = 0; k < count; k++)
            {
                Unpack(values[k], destination, Index(line, start + k, width, columns));
            }
        }
    }

    private static int Index(int line, int position, int width, bool columns)
        => columns ? position * width + line : line * width + position;

    private static bool InRange(float luminance, float low, float high) => luminance >= low && luminance <= high;

    private static float GetKey(byte[] pixels, int pixelIndex, float luminance, int key)
    {
        int o = pixelIndex * 4;
        switch (key)
        {
            case KeyLuminance:
                return luminance;
            case KeyHue:
                PixelSampler.RgbToHsv(pixels[o + 2], pixels[o + 1], pixels[o], out float hue, out _, out _);
                return hue;
            case KeySaturation:
                PixelSampler.RgbToHsv(pixels[o + 2], pixels[o + 1], pixels[o], out _, out float saturation, out _);
                return saturation;
            default:
                return pixels[o + 2];
        }
    }

    private static uint Pack(byte[] pixels, int pixelIndex)
    {
        int o = pixelIndex * 4;
        return (uint)(pixels[o] | (pixels[o + 1] << 8) | (pixels[o + 2] << 16));
    }

    private static void Unpack(uint value, byte[] pixels, int pixelIndex)
    {
        int o = pixelIndex * 4;
        pixels[o] = (byte)value;
        pixels[o + 1] = (byte)(value >> 8);
        pixels[o + 2] = (byte)(value >> 16);
        pixels[o + 3] = BitmapHelper.Opaque;
    }
}
