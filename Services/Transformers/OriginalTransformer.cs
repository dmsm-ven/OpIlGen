using OpIlGen.Localization;
using System.Windows.Media.Imaging;

namespace OpIlGen.Services.Transformers;

/// <summary>Исходное изображение без преобразований.</summary>
public sealed class OriginalTransformer : IImageTransformer
{
    private readonly ILocalizationService _localizer;

    public OriginalTransformer(ILocalizationService localizer)
    {
        _localizer = localizer;
    }

    public string Name => _localizer.Get("transformer.original.name");
    public string Key => "original";
    public string Description => _localizer.Get("transformer.original.description");

    public TransformerVariable[] AvailableCustomVariables => Array.Empty<TransformerVariable>();

    public BitmapSource Transform(BitmapSource source, TransformerVariable[]? customVariables = null) => source;
}
