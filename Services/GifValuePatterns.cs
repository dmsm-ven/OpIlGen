namespace OpIlGen.Services;

/// <summary>Функции, которыми можно заполнить столбец параметра в таблице кадров генератора GIF.</summary>
public enum GifValuePattern
{
    /// <summary>Линейный рост: минимум -> максимум.</summary>
    RampUp,

    /// <summary>Линейный спад: максимум -> минимум.</summary>
    RampDown,

    /// <summary>Синусоида, один период: минимум -> максимум -> минимум.</summary>
    Sine,

    /// <summary>Инвертированная синусоида: максимум -> минимум -> максимум.</summary>
    SineInverted,

    /// <summary>Треугольная волна: минимум -> максимум -> минимум по прямым.</summary>
    Triangle,

    /// <summary>S-кривая (smoothstep): плавный рост с замедлением на концах.</summary>
    SmoothStep
}

public static class GifValuePatterns
{
    /// <summary>Значение функции от 0 до 1 для позиции t (0 - первый кадр, 1 - последний).</summary>
    public static double Evaluate(GifValuePattern pattern, double t)
    {
        t = Math.Clamp(t, 0, 1);

        double value = pattern switch
        {
            GifValuePattern.RampUp => t,
            GifValuePattern.RampDown => 1 - t,
            GifValuePattern.Sine => (1 - Math.Cos(2 * Math.PI * t)) / 2,
            GifValuePattern.SineInverted => (1 + Math.Cos(2 * Math.PI * t)) / 2,
            GifValuePattern.Triangle => 1 - Math.Abs(2 * t - 1),
            GifValuePattern.SmoothStep => t * t * (3 - 2 * t),
            _ => t
        };

        return Math.Clamp(value, 0, 1);
    }

    /// <summary>
    /// Значения для столбца из <paramref name="count"/> кадров: функция растягивается на все кадры
    /// (первый кадр - начало, последний - конец), результат переводится в диапазон параметра
    /// и выравнивается по его шагу, как на ползунке.
    /// </summary>
    public static double[] Fill(GifValuePattern pattern, int count, double min, double max, double step)
    {
        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            double t = count > 1 ? (double)i / (count - 1) : 0;
            double value = min + (max - min) * Evaluate(pattern, t);

            if (step > 0)
            {
                value = min + Math.Round((value - min) / step) * step;
            }

            values[i] = Math.Round(Math.Clamp(value, min, max), 6);
        }

        return values;
    }
}
