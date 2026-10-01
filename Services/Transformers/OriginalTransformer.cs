using System.Windows.Media.Imaging;

namespace OpIlGen.Services.Transformers;

/// <summary>Исходное изображение без преобразований.</summary>
public sealed class OriginalTransformer : IImageTransformer
{
    public string Name => "Оригинальное фото";
    public string Key => "original";
    public string Description => "Исходное изображение без каких-либо преобразований.";

    public BitmapSource Transform(BitmapSource source) => source;
}
