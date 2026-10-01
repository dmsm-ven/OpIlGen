using System.Windows.Media.Imaging;

namespace OpIlGen.Services;

/// <summary>Преобразователь изображения (оптическая иллюзия).</summary>
public interface IImageTransformer
{
    /// <summary>Название для отображения в UI.</summary>
    string Name { get; }

    /// <summary>Короткий идентификатор, используется в имени выходного файла.</summary>
    string Key { get; }

    BitmapSource Transform(BitmapSource source);
}
