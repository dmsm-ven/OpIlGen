using System.Windows.Media.Imaging;

namespace OpIlGen.Services.Transformers;

/// <summary>
/// Базовый класс для попиксельных преобразователей: читает Bgra32, сводит прозрачность к белому фону,
/// вызывает <see cref="Process"/> и собирает результат обратно в BitmapSource.
/// </summary>
public abstract class PixelTransformerBase : IImageTransformer
{
    public abstract string Name { get; }
    public abstract string Key { get; }
    public abstract string Description { get; }

    public BitmapSource Transform(BitmapSource source)
    {
        var pixels = BitmapHelper.ReadBgra32(source, out int width, out int height);
        BitmapHelper.FlattenOnWhite(pixels);

        var result = Process(pixels, width, height);
        return BitmapHelper.CreateBgra32(result, width, height, source);
    }

    /// <summary>Получает пиксели Bgra32 (alpha = 255), возвращает пиксели того же размера.</summary>
    protected abstract byte[] Process(byte[] pixels, int width, int height);
}
