using OpIlGen.Localization;
using System.Windows.Media.Imaging;

namespace OpIlGen.Services.Transformers;

/// <summary>
/// Базовый класс для попиксельных преобразователей: читает Bgra32, сводит прозрачность к белому фону,
/// вызывает <see cref="Process"/> и собирает результат обратно в BitmapSource.
/// </summary>
public abstract class PixelTransformerBase : IImageTransformer, IPixelBufferTransformer
{
    /// <summary>Подготовленные пиксели последнего исходного изображения (Bgra32, alpha = 255).</summary>
    private sealed record PreparedInput(WeakReference<BitmapSource> Source, byte[] Pixels, int Width, int Height);

    // Живой режим вызывает Transform десятки раз в секунду для одной и той же картинки: конвертация,
    // копирование и сведение прозрачности каждый раз - чистая потеря времени. Кэш из одного элемента,
    // подмена ссылки атомарна, поэтому вызов из нескольких потоков безопасен (в худшем случае
    // подготовка выполнится дважды).
    private volatile PreparedInput? _prepared;

    protected PixelTransformerBase(ILocalizationService localizer)
    {
        Localizer = localizer;
    }

    /// <summary>Локализация: названия и описания берутся по ключам (transformer.&lt;key&gt;.name и т.д.).</summary>
    protected ILocalizationService Localizer { get; }

    public abstract string Name { get; }
    public abstract string Key { get; }
    public abstract string Description { get; }

    /// <summary>По умолчанию настраиваемых параметров нет. Переопределите, чтобы добавить их.</summary>
    public virtual TransformerVariable[] AvailableCustomVariables => Array.Empty<TransformerVariable>();

    public BitmapSource Transform(BitmapSource source, TransformerVariable[]? customVariables = null)
    {
        var result = TransformToPixels(source, customVariables, out int width, out int height);
        return BitmapHelper.CreateBgra32(result, width, height, source);
    }

    public byte[] TransformToPixels(
        BitmapSource source,
        TransformerVariable[]? customVariables,
        out int width,
        out int height)
    {
        var input = GetPreparedInput(source);
        width = input.Width;
        height = input.Height;
        return Process(input.Pixels, width, height, customVariables);
    }

    private PreparedInput GetPreparedInput(BitmapSource source)
    {
        var cached = _prepared;
        if (cached is not null && cached.Source.TryGetTarget(out var cachedSource) && ReferenceEquals(cachedSource, source))
        {
            return cached;
        }

        var pixels = BitmapHelper.ReadBgra32(source, out int width, out int height);
        BitmapHelper.FlattenOnWhite(pixels);
        var prepared = new PreparedInput(new WeakReference<BitmapSource>(source), pixels, width, height);

        // Кэшируем только неизменяемые изображения: у WriteableBitmap содержимое может поменяться
        // при той же ссылке, и кэш выдал бы устаревшие пиксели.
        if (source.IsFrozen)
        {
            _prepared = prepared;
        }

        return prepared;
    }

    /// <summary>
    /// Получает пиксели Bgra32 (alpha = 255), возвращает пиксели того же размера.
    /// Массив <paramref name="pixels"/> общий для всех кадров (кэш): его нельзя изменять и нельзя возвращать
    /// как результат. Результат нужно писать в новый массив.
    /// </summary>
    protected abstract byte[] Process(byte[] pixels, int width, int height, TransformerVariable[]? customVariables);
}
