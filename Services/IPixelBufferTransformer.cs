using System.Windows.Media.Imaging;

namespace OpIlGen.Services;

/// <summary>
/// Преобразователь, умеющий отдавать результат как массив пикселей Bgra32 (4 байта на пиксель: B, G, R, A),
/// минуя создание нового BitmapSource. Нужен «живым» режимам (музыкальное окно): они выводят кадры
/// в один и тот же WriteableBitmap, и WPF не пересоздаёт текстуру на каждый кадр.
/// </summary>
public interface IPixelBufferTransformer : IImageTransformer
{
    /// <summary>
    /// То же, что <see cref="IImageTransformer.Transform"/>, но возвращает пиксели (stride = width * 4).
    /// Можно вызывать из фонового потока. Возвращённый массив принадлежит вызывающему.
    /// </summary>
    byte[] TransformToPixels(
        BitmapSource source,
        TransformerVariable[]? customVariables,
        out int width,
        out int height);
}
