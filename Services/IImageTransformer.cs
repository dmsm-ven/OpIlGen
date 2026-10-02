using System.Windows.Media.Imaging;

namespace OpIlGen.Services;

/// <summary>Преобразователь изображения (оптическая иллюзия).</summary>
public interface IImageTransformer
{
    /// <summary>Название для отображения в UI.</summary>
    string Name { get; }

    /// <summary>Короткий идентификатор, используется в имени выходного файла.</summary>
    string Key { get; }

    /// <summary>Короткое описание иллюзии и подсказка, как на неё смотреть.</summary>
    string Description { get; }

    /// <summary>Настраиваемые параметры преобразователя (для каждого в UI выводится ползунок). Может быть пустым.</summary>
    TransformerVariable[] AvailableCustomVariables { get; }

    /// <summary>
    /// Применяет преобразование. <paramref name="customVariables"/> - значения параметров, выбранные пользователем;
    /// если null или параметр не передан, используется его значение по умолчанию.
    /// </summary>
    BitmapSource Transform(BitmapSource source, TransformerVariable[]? customVariables = null);
}

/// <summary>Описание настраиваемого параметра преобразователя и его текущее значение.</summary>
public class TransformerVariable
{
    private double? _value;

    /// <summary>Название для отображения в UI, например "Блоков по длинной стороне".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Ключ параметра, например "blocks_on_long_side". Используется для поиска значения.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Подробное описание: за что отвечает параметр.</summary>
    public string Description { get; set; } = string.Empty;

    public double MinValue { get; set; }

    /// <summary>Значение по умолчанию (то же, что в const-переменных преобразователя).</summary>
    public double DefaultValue { get; set; }

    public double MaxValue { get; set; }

    /// <summary>Шаг ползунка в UI.</summary>
    public double Step { get; set; } = 1;

    /// <summary>Текущее значение. Пока не задано явно, равно <see cref="DefaultValue"/>.</summary>
    public double Value
    {
        get => _value ?? DefaultValue;
        set => _value = value;
    }
}

public static class TransformerVariableExtensions
{
    /// <summary>
    /// Возвращает значение параметра <paramref name="definition"/> из переданных пользовательских значений
    /// (поиск по Key). Если значения нет, берётся значение по умолчанию. Результат ограничен Min/Max.
    /// </summary>
    public static double GetValue(this TransformerVariable[]? variables, TransformerVariable definition)
    {
        var match = variables?.FirstOrDefault(v => v.Key == definition.Key);
        double value = match?.Value ?? definition.DefaultValue;
        return Math.Clamp(value, definition.MinValue, definition.MaxValue);
    }
}
