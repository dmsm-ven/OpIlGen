using OpIlGen.Services;

namespace OpIlGen.Localization;

public static class LocalizationExtensions
{
    /// <summary>Название параметра преобразователя на текущем языке (по NameKey, иначе Name).</summary>
    public static string GetName(this ILocalizationService localizer, TransformerVariable variable) =>
        string.IsNullOrEmpty(variable.NameKey) ? variable.Name : localizer.Get(variable.NameKey);

    /// <summary>Описание параметра преобразователя на текущем языке (по DescriptionKey, иначе Description).</summary>
    public static string GetDescription(this ILocalizationService localizer, TransformerVariable variable) =>
        string.IsNullOrEmpty(variable.DescriptionKey) ? variable.Description : localizer.Get(variable.DescriptionKey);
}
