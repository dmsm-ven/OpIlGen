namespace OpIlGen.Services;

/// <summary>
/// Правило "параметр преобразователя зависит от характеристики звука":
/// уровень 0 даёт минимум параметра, уровень 1 - максимум.
/// </summary>
public sealed class AudioReactiveMapping
{
    public AudioReactiveMapping(TransformerVariable variable, AudioFeature feature)
    {
        Variable = variable;
        Feature = feature;
    }

    public TransformerVariable Variable { get; }

    public AudioFeature Feature { get; }

    /// <summary>Переводит уровень звука (0..1) в значение параметра (Min..Max с учётом шага).</summary>
    public double Map(double level)
    {
        level = Math.Clamp(level, 0, 1);

        double min = Variable.MinValue;
        double max = Variable.MaxValue;
        double value = min + level * (max - min);

        if (Variable.Step > 0)
        {
            value = min + Math.Round((value - min) / Variable.Step) * Variable.Step;
        }

        return Math.Clamp(value, min, max);
    }
}
