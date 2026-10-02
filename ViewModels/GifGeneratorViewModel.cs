using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpIlGen.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpIlGen.ViewModels;

/// <summary>Окно генератора GIF: серия кадров (контрольных точек) с параметрами преобразователя.</summary>
public partial class GifGeneratorViewModel : ObservableObject
{
    private const double DefaultFrameDelayMs = 200;

    /// <summary>Минимальное число кадров, чтобы получилась анимация.</summary>
    private const int MinFrames = 2;

    /// <summary>Исходное изображение уменьшается, если его длинная сторона больше (быстрее и меньше файл).</summary>
    private const int MaxGifSide = 1024;

    private readonly string _sourcePath;
    private readonly IImageTransformer _transformer;
    private readonly IImageService _imageService;
    private readonly IGifService _gifService;

    public GifGeneratorViewModel(
        string sourcePath,
        IImageTransformer transformer,
        IImageService imageService,
        IGifService gifService)
    {
        _sourcePath = sourcePath;
        _transformer = transformer;
        _imageService = imageService;
        _gifService = gifService;
    }

    public string SourceName => Path.GetFileName(_sourcePath);

    public string TransformerName => _transformer.Name;

    /// <summary>У преобразователя нет параметров - все кадры получатся одинаковыми.</summary>
    public bool HasNoVariables => _transformer.AvailableCustomVariables.Length == 0;

    public ObservableCollection<GifFrameViewModel> Frames { get; } = new();

    [ObservableProperty]
    private double _frameDelayMs = DefaultFrameDelayMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(AddFrameCommand))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private string _status = "Нажмите «+», чтобы добавить кадр, и задайте для него параметры преобразователя.";

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private void AddFrame()
    {
        // Новый кадр начинается со значений предыдущего - удобно менять один параметр от кадра к кадру
        var previous = Frames.LastOrDefault();
        Frames.Add(new GifFrameViewModel(_transformer.AvailableCustomVariables, previous, RemoveFrame));

        Renumber();
        Status = $"Кадров: {Frames.Count}. Нажмите «Склеить», когда всё готово.";
    }

    private void RemoveFrame(GifFrameViewModel frame)
    {
        Frames.Remove(frame);

        Renumber();
        Status = $"Кадров: {Frames.Count}.";
    }

    private void Renumber()
    {
        for (int i = 0; i < Frames.Count; i++)
        {
            Frames[i].Number = i + 1;
        }
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (Frames.Count < MinFrames)
        {
            Status = $"Для анимации нужно минимум {MinFrames} кадра: добавьте их кнопкой «+».";
            return;
        }

        IsBusy = true;

        var frameVariables = Frames.Select(f => f.ToModels()).ToArray();
        int delay = (int)Math.Round(FrameDelayMs);
        var progress = new Progress<int>(done => Status = $"Обработка кадра {done} из {frameVariables.Length}...");

        try
        {
            var gifPath = await Task.Run(() =>
            {
                var source = LoadScaledSource();

                // Кадры создаются лениво: GifService перебирает их по одному и сразу кодирует
                var frames = frameVariables.Select((variables, index) =>
                {
                    progress.Report(index + 1);
                    return _transformer.Transform(source, variables);
                });

                return _gifService.Save(frames, _sourcePath, delay);
            });

            Status = $"Готово: {gifPath}";
        }
        catch (Exception ex)
        {
            Status = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private BitmapSource LoadScaledSource()
    {
        var source = _imageService.Load(_sourcePath);

        int longSide = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longSide <= MaxGifSide)
        {
            return source;
        }

        double scale = (double)MaxGifSide / longSide;
        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }
}

/// <summary>Один кадр (контрольная точка): собственный набор значений параметров преобразователя.</summary>
public partial class GifFrameViewModel : ObservableObject
{
    private readonly Action<GifFrameViewModel> _remove;

    public GifFrameViewModel(
        IEnumerable<TransformerVariable> definitions,
        GifFrameViewModel? previous,
        Action<GifFrameViewModel> remove)
    {
        _remove = remove;

        Variables = definitions
            .Select(definition => new TransformerVariableViewModel(definition, () => { }))
            .ToArray();

        if (previous is not null)
        {
            // Параметры в обоих кадрах созданы из одного и того же списка, порядок совпадает
            for (int i = 0; i < Variables.Count; i++)
            {
                Variables[i].Value = previous.Variables[i].Value;
            }
        }
    }

    public IReadOnlyList<TransformerVariableViewModel> Variables { get; }

    /// <summary>Порядковый номер кадра для отображения (с единицы).</summary>
    [ObservableProperty]
    private int _number;

    [RelayCommand]
    private void Remove() => _remove(this);

    /// <summary>Значения параметров этого кадра для передачи в Transform.</summary>
    public TransformerVariable[] ToModels() => Variables.Select(v => v.ToModel()).ToArray();
}
