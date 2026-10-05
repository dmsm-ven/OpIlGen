using CommunityToolkit.Mvvm.ComponentModel;
using OpIlGen.Localization;
using OpIlGen.Services;

namespace OpIlGen.ViewModels;

/// <summary>Элемент списка преобразователей: название обновляется при смене языка, есть отметка «избранное».</summary>
public sealed partial class TransformerItemViewModel : ObservableObject
{
    private readonly Action<TransformerItemViewModel>? _onFavoriteChanged;

    public TransformerItemViewModel(
        IImageTransformer transformer,
        ILocalizationService localizer,
        bool isFavorite = false,
        Action<TransformerItemViewModel>? onFavoriteChanged = null)
    {
        Transformer = transformer;
        _isFavorite = isFavorite;
        _onFavoriteChanged = onFavoriteChanged;

        localizer.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
        };
    }

    public IImageTransformer Transformer { get; }

    public string Name => Transformer.Name;

    public string Description => Transformer.Description;

    /// <summary>Избранные преобразователи при сортировке всегда стоят выше остальных.</summary>
    [ObservableProperty]
    private bool _isFavorite;

    partial void OnIsFavoriteChanged(bool value) => _onFavoriteChanged?.Invoke(this);
}
