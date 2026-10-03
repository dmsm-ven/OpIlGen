using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpIlGen.Localization;
using OpIlGen.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpIlGen.ViewModels;

/// <summary>
/// Окно генератора GIF: таблица кадров (строка = кадр, столбец = параметр преобразователя),
/// общие настройки задержки и перехода между кадрами.
/// </summary>
public partial class GifGeneratorViewModel : ObservableObject
{
    private const double DefaultFrameDelayMs = 200;

    /// <summary>Сколько промежуточных кадров добавляется на каждый переход по умолчанию.</summary>
    private const double DefaultTransitionFrames = 5;

    /// <summary>Минимальное число кадров, чтобы получилась анимация (для Fade хватит одного).</summary>
    private const int MinFrames = 2;
    private const int MinFramesForFade = 1;

    /// <summary>Исходное изображение уменьшается, если его длинная сторона больше (быстрее и меньше файл).</summary>
    private const int MaxGifSide = 1024;

    private readonly string _sourcePath;
    private readonly IImageTransformer _transformer;
    private readonly IImageService _imageService;
    private readonly IGifService _gifService;
    private readonly IShellService _shell;
    private readonly ILocalizationService _localizer;

    public GifGeneratorViewModel(
        string sourcePath,
        IImageTransformer transformer,
        IImageService imageService,
        IGifService gifService,
        IShellService shellService,
        ILocalizationService localizer)
    {
        _sourcePath = sourcePath;
        _transformer = transformer;
        _imageService = imageService;
        _gifService = gifService;
        _shell = shellService;
        _localizer = localizer;
        _status = localizer.Get("gif.status.initial");

        Frames.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Summary));
    }

    public string SourceName => Path.GetFileName(_sourcePath);

    public string TransformerName => _transformer.Name;

    /// <summary>Столбцы таблицы: параметры преобразователя.</summary>
    public IReadOnlyList<GifColumn> Columns => _transformer.AvailableCustomVariables
        .Select(v => new GifColumn(_localizer.GetName(v), _localizer.GetDescription(v), v.MinValue, v.MaxValue))
        .ToArray();

    /// <summary>У преобразователя нет параметров - кадры отличаются только яркостью в режиме Fade.</summary>
    public bool HasNoVariables => _transformer.AvailableCustomVariables.Length == 0;

    /// <summary>Строки таблицы: кадры.</summary>
    public ObservableCollection<GifFrameViewModel> Frames { get; } = new();

    [ObservableProperty]
    private double _frameDelayMs = DefaultFrameDelayMs;

    // ---- Переход между кадрами ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNoTransition), nameof(IsSmoothTransition), nameof(IsFadeTransition))]
    [NotifyPropertyChangedFor(nameof(HasTransition), nameof(TransitionHint), nameof(Summary))]
    private GifTransition _transition = GifTransition.None;

    /// <summary>Сколько промежуточных кадров добавляется на каждый переход.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private double _transitionFrames = DefaultTransitionFrames;

    // Свойства для RadioButton: выбранный вариант - один из трёх
    public bool IsNoTransition
    {
        get => Transition == GifTransition.None;
        set { if (value) Transition = GifTransition.None; }
    }

    public bool IsSmoothTransition
    {
        get => Transition == GifTransition.Smooth;
        set { if (value) Transition = GifTransition.Smooth; }
    }

    public bool IsFadeTransition
    {
        get => Transition == GifTransition.Fade;
        set { if (value) Transition = GifTransition.Fade; }
    }

    public bool HasTransition => Transition != GifTransition.None;

    public string TransitionHint => Transition switch
    {
        GifTransition.Smooth => _localizer.Get("gif.transition.hint.smooth"),
        GifTransition.Fade => _localizer.Get("gif.transition.hint.fade"),
        _ => _localizer.Get("gif.transition.hint.none")
    };

    /// <summary>Сколько кадров получится в итоговом GIF.</summary>
    public string Summary =>
        _localizer.Format("gif.summary", GifFramePlanner.CountFrames(Frames.Count, Transition, (int)Math.Round(TransitionFrames)));

    // ---- Состояние ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(AddFrameCommand))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private string _status = string.Empty;

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private void AddFrame()
    {
        // Новая строка начинается со значений предыдущей - удобно менять один параметр от кадра к кадру
        var previous = Frames.LastOrDefault();
        Frames.Add(new GifFrameViewModel(_transformer.AvailableCustomVariables, previous, RemoveFrame));

        Renumber();
        Status = _localizer.Format("gif.status.frames_added", Frames.Count);
    }

    private void RemoveFrame(GifFrameViewModel frame)
    {
        Frames.Remove(frame);

        Renumber();
        Status = _localizer.Format("gif.status.frames", Frames.Count);
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
        int requiredFrames = Transition == GifTransition.Fade ? MinFramesForFade : MinFrames;
        if (Frames.Count < requiredFrames)
        {
            Status = _localizer.Format("gif.status.need_frames", requiredFrames);
            return;
        }

        IsBusy = true;

        // Всё нужное копируется до перехода в фоновый поток
        var keyframes = Frames.Select(f => f.ToModels()).ToArray();
        var plan = GifFramePlanner.Plan(keyframes, Transition, (int)Math.Round(TransitionFrames));
        int delay = (int)Math.Round(FrameDelayMs);
        IProgress<int> progress = new Progress<int>(done => Status = _localizer.Format("gif.status.processing", done, plan.Count));

        try
        {
            var gifPath = await Task.Run(() =>
            {
                var source = LoadScaledSource();

                // Соседние кадры с одинаковыми параметрами (Fade) рендерятся один раз
                TransformerVariable[]? lastVariables = null;
                BitmapSource? lastRendered = null;

                // Кадры создаются лениво: GifService перебирает их по одному и сразу кодирует
                var frames = plan.Select((spec, index) =>
                {
                    progress.Report(index + 1);

                    if (!ReferenceEquals(spec.Variables, lastVariables))
                    {
                        lastRendered = _transformer.Transform(source, spec.Variables);
                        lastVariables = spec.Variables;
                    }

                    return BitmapEffects.Dim(lastRendered!, spec.Brightness);
                });

                return _gifService.Save(frames, _sourcePath, delay);
            });

            Status = _localizer.Format("gif.status.done", gifPath);
        }
        catch (Exception ex)
        {
            Status = _localizer.Format("status.error", ex.Message);
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

    [RelayCommand]
    private void OpenOutputFolder()
    {
        _shell.OpenFolder(_gifService.OutputDirectory);
    }
}

/// <summary>Строка таблицы: один кадр (номер, значения параметров и кнопка удаления).</summary>
public partial class GifFrameViewModel : ObservableObject
{
    private readonly Action<GifFrameViewModel> _remove;

    public GifFrameViewModel(
        IEnumerable<TransformerVariable> definitions,
        GifFrameViewModel? previous,
        Action<GifFrameViewModel> remove)
    {
        _remove = remove;

        Cells = definitions.Select(definition => new GifCellViewModel(definition)).ToArray();

        if (previous is not null)
        {
            // Ячейки в обеих строках созданы из одного и того же списка, порядок совпадает
            for (int i = 0; i < Cells.Count; i++)
            {
                Cells[i].Value = previous.Cells[i].Value;
            }
        }
    }

    /// <summary>Ячейки строки - по одной на параметр преобразователя.</summary>
    public IReadOnlyList<GifCellViewModel> Cells { get; }

    /// <summary>Порядковый номер кадра для отображения (с единицы).</summary>
    [ObservableProperty]
    private int _number;

    [RelayCommand]
    private void Remove() => _remove(this);

    /// <summary>Значения параметров этого кадра для передачи в Transform.</summary>
    public TransformerVariable[] ToModels() => Cells.Select(c => c.ToModel()).ToArray();
}

/// <summary>Ячейка таблицы: значение одного параметра в одном кадре. Значение ограничивается границами параметра.</summary>
public sealed class GifCellViewModel : ObservableObject
{
    private readonly TransformerVariable _definition;
    private double _value;

    public GifCellViewModel(TransformerVariable definition)
    {
        _definition = definition;
        _value = definition.DefaultValue;
    }

    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, _definition.MinValue, _definition.MaxValue);

            // Уведомляем всегда, чтобы TextBox показал исправленное значение, даже если оно не изменилось
            OnPropertyChanged();
        }
    }

    public TransformerVariable ToModel() => _definition.WithValue(_value);
}

/// <summary>Заголовок столбца таблицы: параметр преобразователя (текст уже на текущем языке).</summary>
public sealed record GifColumn(string Name, string Description, double MinValue, double MaxValue);
