namespace OpIlGen.Services.Transformers;

/// <summary>Создаёт описание параметра с ключами локализации по шаблону transformer.&lt;id&gt;.var.&lt;key&gt;.*</summary>
internal static class TransformerVariableFactory
{
    public static TransformerVariable Create(
        string transformerId, string key, double min, double defaultValue, double max, double step) => new()
    {
        NameKey = $"transformer.{transformerId}.var.{key}.name",
        Key = key,
        DescriptionKey = $"transformer.{transformerId}.var.{key}.description",
        MinValue = min,
        DefaultValue = defaultValue,
        MaxValue = max,
        Step = step
    };
}
