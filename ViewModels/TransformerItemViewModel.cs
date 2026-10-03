using CommunityToolkit.Mvvm.ComponentModel;
using OpIlGen.Localization;
using OpIlGen.Services;

namespace OpIlGen.ViewModels;

/// <summary>Элемент списка преобразователей: название обновляется при смене языка.</summary>
public sealed class TransformerItemViewModel : ObservableObject
{
    public TransformerItemViewModel(IImageTransformer transformer, ILocalizationService localizer)
    {
        Transformer = transformer;

        localizer.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
        };
    }

    public IImageTransformer Transformer { get; }

    public string Name => Transformer.Name;

    public string Description => Transformer.Description;
}
