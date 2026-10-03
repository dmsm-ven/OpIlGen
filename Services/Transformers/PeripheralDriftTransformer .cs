
namespace OpIlGen.Services.Transformers;

/// <summary>
/// Иллюзия периферического дрейфа (Peripheral Drift).
/// Концентрические кольца состоят из повторяющихся сегментов
/// с последовательностью яркости: чёрный -> тёмный -> белый -> светлый.
/// При фиксации взгляда и движении глаз может возникать ощущение вращения.
/// </summary>
public sealed class PeripheralDriftTransformer : PixelTransformerBase
{
    private const int DefaultRingsAcross = 12;
    private const int DefaultSegmentsPerRing = 64;
    private const int DefaultPatternStrength = 80;

    private const int PercentScale = 100;
    private const double TwoPi = Math.PI * 2.0;

    // Последовательность яркости сегментов:
    // чёрный -> тёмно-серый -> белый -> светло-серый.
    private static readonly byte[] Pattern =
    [
        0, 85, 255, 170
    ];

    private static readonly TransformerVariable RingsVariable = new()
    {
        Name = "Колец по радиусу",
        Key = "rings_across",
        Description = "Количество концентрических колец. " +
                      "Больше колец - более мелкий и частый узор.",
        MinValue = 4,
        DefaultValue = DefaultRingsAcross,
        MaxValue = 24,
        Step = 1
    };

    private static readonly TransformerVariable SegmentsVariable = new()
    {
        Name = "Сегментов на кольцо",
        Key = "segments_per_ring",
        Description = "Количество радиальных сегментов в каждом кольце. " +
                      "Большее значение создаёт более мелкий узор.",
        MinValue = 32,
        DefaultValue = DefaultSegmentsPerRing,
        MaxValue = 128,
        Step = 8
    };

    private static readonly TransformerVariable StrengthVariable = new()
    {
        Name = "Сила узора, %",
        Key = "pattern_strength",
        Description = "Степень смешивания узора с исходным изображением. " +
                      "При 100% исходная фотография полностью заменяется узором.",
        MinValue = 30,
        DefaultValue = DefaultPatternStrength,
        MaxValue = 100,
        Step = 5
    };

    public override string Name => "Периферический дрейф";

    public override string Key => "peripheral_drift";

    public override string Description =>
        "Концентрические кольца из чередующихся светлых и тёмных сегментов. " +
        "Статичный узор может восприниматься как вращающийся при периферическом " +
        "зрении или небольших движениях глаз.";

    public override TransformerVariable[] AvailableCustomVariables { get; } =
    [
        RingsVariable,
        SegmentsVariable,
        StrengthVariable
    ];

    protected override byte[] Process(
        byte[] pixels,
        int width,
        int height,
        TransformerVariable[]? customVariables)
    {
        int ringsAcross = (int)Math.Round(
            customVariables.GetValue(RingsVariable));

        int segments = (int)Math.Round(
            customVariables.GetValue(SegmentsVariable));

        float strength = (float)(
            customVariables.GetValue(StrengthVariable) / PercentScale);

        float originalWeight = 1f - strength;

        var result = new byte[pixels.Length];

        // Центр изображения.
        double cx = width / 2.0;
        double cy = height / 2.0;

        // Нормализация радиуса относительно меньшей стороны.
        double radiusScale = Math.Min(width, height) / 2.0;

        // Предварительно вычисляем угловые координаты для каждого X.
        var angles = new double[width];

        for (int x = 0; x < width; x++)
        {
            double angle = Math.Atan2(
                cy - 0.5,
                x + 0.5 - cx);

            if (angle < 0)
                angle += TwoPi;

            angles[x] = angle;
        }

        for (int y = 0; y < height; y++)
        {
            double dy = y + 0.5 - cy;

            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;

                double dx = x + 0.5 - cx;

                // Расстояние от центра изображения.
                double radius = Math.Sqrt(dx * dx + dy * dy);

                // Индекс концентрического кольца.
                int ring = (int)(radius / radiusScale * ringsAcross);

                // Угловой сегмент.
                int segment = (int)(
                    angles[x] * segments / TwoPi);

                segment = Math.Min(segment, segments - 1);

                // Соседние кольца имеют противоположную фазу.
                int phase = (segment + ((ring & 1) * 2)) & 3;

                float patternValue = Pattern[phase];

                // Исходная яркость фотографии.
                float originalValue =
                    BitmapHelper.BlueWeight * pixels[i] +
                    BitmapHelper.GreenWeight * pixels[i + 1] +
                    BitmapHelper.RedWeight * pixels[i + 2];

                // Смешиваем исходное изображение с узором.
                byte value = (byte)Math.Clamp(
                    (int)Math.Round(
                        originalValue * originalWeight +
                        patternValue * strength),
                    0,
                    255);

                // Получаем монохромный оптический узор.
                result[i] = value;
                result[i + 1] = value;
                result[i + 2] = value;
                result[i + 3] = BitmapHelper.Opaque;
            }
        }

        return result;
    }
}