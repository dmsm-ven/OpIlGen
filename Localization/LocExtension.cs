using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace OpIlGen.Localization;

/// <summary>
/// Расширение разметки для переводимых строк: <c>Text="{loc:Loc main.settings}"</c>.
/// Строка привязывается к <see cref="LocalizedText"/>, поэтому при смене языка обновляется сама.
/// Подключение пространства имён: <c>xmlns:loc="clr-namespace:OpIlGen.Localization"</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var localizer = (Application.Current as App)?.Services.GetService<ILocalizationService>();
        if (localizer is null)
        {
            // Конструктор разметки в дизайнере: показываем ключ
            return Key;
        }

        var binding = new Binding(nameof(LocalizedText.Value))
        {
            Source = localizer.GetObservable(Key),
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}
