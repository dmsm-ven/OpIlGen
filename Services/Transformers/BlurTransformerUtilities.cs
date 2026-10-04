namespace OpIlGen.Services.Transformers;

internal static class BlurTransformerUtilities
{
    public static byte[] BoxBlurPixels(byte[] source, int width, int height, int radius)
    {
        var result = (byte[])source.Clone();
        for (int channel = 0; channel < 3; channel++)
        {
            var plane = new float[width * height];
            for (int p = 0; p < plane.Length; p++)
                plane[p] = source[p * 4 + channel];

            BitmapHelper.BoxBlur(plane, width, height, radius);

            for (int p = 0; p < plane.Length; p++)
                result[p * 4 + channel] = (byte)Math.Clamp((int)Math.Round(plane[p]), 0, 255);
        }
        return result;
    }

    public static byte Blend(byte original, byte processed, double amount)
        => (byte)Math.Clamp((int)Math.Round(original * (1.0 - amount) + processed * amount), 0, 255);

    public static float Luminance(byte[] pixels, int pixel)
    {
        int i = pixel * 4;
        return BitmapHelper.BlueWeight * pixels[i]
             + BitmapHelper.GreenWeight * pixels[i + 1]
             + BitmapHelper.RedWeight * pixels[i + 2];
    }

    public static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);
}
