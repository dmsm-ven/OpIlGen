using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpIlGen.Localization;
using OpIlGen.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OpIlGen.ViewModels;

/// <summary>
/// Окно музыкального режима: во время воспроизведения параметры преобразователя
/// меняются в зависимости от выбранных характеристик звука (Audio Reactive Mapping).
/// </summary>
public partial class MusicViewModel : ObservableObject
{
    /// <summary>Картинка уменьшается до этой длинной стороны, чтобы пересчёт успевал ~10 раз в секунду.</summary>
    private const int PreviewMaxSide = 640;

    private const int UpdateIntervalMs = 100;

    /// <summary>Сглаживание: доля пути к новому уровню за один шаг (быстро растёт, медленнее спадает).</summary>
    private const double AttackFactor = 0.7;
    private const double ReleaseFactor = 0.3;

    private readonly IFileDialogService _fileDialog;
    private readonly IImageService _imageService;
    private readonly IAudioAnalysisService _analysisService;
    private readonly ILocalizationService _localizer;
    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<AudioFeature, double> _smoothed = new();

    private BitmapSource? _baseImage;
    private CancellationTokenSource? _analysisCts;
    private bool _isRendering;

    public MusicViewModel(
        string sourcePath,
        IImageTransformer transformer,
        IFileDialogService fileDialog,
        IImageService imageService,
        IAudioAnalysisService analysisService,
        ILocalizationService localizer)
    {
        SourcePath = sourcePath;
        Transformer = transformer;
        _fileDialog = fileDialog;
        _imageService = imageService;
        _analysisService = analysisService;
        _localizer = localizer;
        _status = localizer.Get("music.status.initial");

        var options = new[]
        {
            new AudioSourceOption(AudioFeature.None, localizer.Get("music.source.none")),
            new AudioSourceOption(AudioFeature.Bass, localizer.Get("music.source.bass"))
        };
        Mappings = new ObservableCollection<MusicMappingRowViewModel>(
            transformer.AvailableCustomVariables.Select(v => new MusicMappingRowViewModel(v, localizer, options)));

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(UpdateIntervalMs) };
        _timer.Tick += OnTimerTick;

        _player.MediaFailed += (_, e) =>
        {
            StopInternal();
            Status = string.Format(_localizer.Get("music.status.error"), e.ErrorException?.Message);
        };
        _player.MediaEnded += (_, _) =>
        {
            StopInternal();
            Status = _localizer.Get("music.status.ended");
        };

        LoadBaseImage();
    }

    /// <summary>Путь к изображению, выбранному на главном окне.</summary>
    public string SourcePath { get; }

    /// <summary>Выбранный на главном окне преобразователь.</summary>
    public IImageTransformer Transformer { get; }

    public string SourceName => Path.GetFileName(SourcePath);

    public string TransformerName => Transformer.Name;

    /// <summary>Таблица зависимостей: по строке на каждый параметр преобразователя.</summary>
    public ObservableCollection<MusicMappingRowViewModel> Mappings { get; }

    public bool HasMappings => Mappings.Count > 0;

    [ObservableProperty]
    private BitmapSource? _resultImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MusicName))]
    private string? _musicPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private AudioTrackAnalysis? _analysis;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(StopCommand))]
    private bool _isPlaying;

    [ObservableProperty]
    private string _status;

    public string MusicName => string.IsNullOrEmpty(MusicPath) ? "-" : Path.GetFileName(MusicPath);

    private void LoadBaseImage()
    {
        try
        {
            _baseImage = CreatePreview(_imageService.Load(SourcePath));
            ResultImage = Transformer.Transform(_baseImage);
        }
        catch (Exception ex)
        {
            Status = string.Format(_localizer.Get("status.error"), ex.Message);
        }
    }

    private static BitmapSource CreatePreview(BitmapSource source)
    {
        double longSide = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longSide <= PreviewMaxSide)
        {
            return source;
        }

        double scale = PreviewMaxSide / longSide;
        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    [RelayCommand]
    private async Task SelectMusicAsync()
    {
        var path = _fileDialog.PickAudioFile();
        if (path is null)
        {
            return;
        }

        StopInternal();
        _analysisCts?.Cancel();
        var cts = _analysisCts = new CancellationTokenSource();

        MusicPath = path;
        Analysis = null;
        IsAnalyzing = true;
        Status = _localizer.Get("music.status.analyzing");

        try
        {
            var result = await _analysisService.AnalyzeAsync(path, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            Analysis = result;
            Status = _localizer.Get("music.status.ready");
        }
        catch (OperationCanceledException)
        {
            // Выбран другой файл или окно закрыто
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
            {
                Status = string.Format(_localizer.Get("music.status.analysis_error"), ex.Message);
            }
        }
        finally
        {
            if (_analysisCts == cts)
            {
                IsAnalyzing = false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play()
    {
        if (string.IsNullOrEmpty(MusicPath))
        {
            return;
        }

        _smoothed.Clear();
        _player.Open(new Uri(MusicPath, UriKind.Absolute));
        _player.Play();
        IsPlaying = true;
        _timer.Start();
        Status = _localizer.Get("music.status.playing");
    }

    private bool CanPlay() => Analysis is not null && !IsPlaying;

    [RelayCommand(CanExecute = nameof(IsPlaying))]
    private void Stop()
    {
        StopInternal();
        Status = _localizer.Get("music.status.stopped");
    }

    private void StopInternal()
    {
        _timer.Stop();
        _player.Stop();
        _player.Close();
        IsPlaying = false;

        // Возвращаем картинку к значениям по умолчанию
        if (_baseImage is not null)
        {
            ResultImage = Transformer.Transform(_baseImage);
        }

        foreach (var row in Mappings)
        {
            row.CurrentValueText = string.Empty;
        }
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        // Пока не досчитан предыдущий кадр, новый пропускаем
        if (_isRendering || Analysis is null || _baseImage is null || !IsPlaying)
        {
            return;
        }

        _isRendering = true;
        try
        {
            var variables = BuildVariables(Analysis, _player.Position);
            var source = _baseImage;
            var image = await Task.Run(() => Transformer.Transform(source, variables));

            if (IsPlaying)
            {
                ResultImage = image;
            }
        }
        catch (Exception ex)
        {
            Status = string.Format(_localizer.Get("status.error"), ex.Message);
        }
        finally
        {
            _isRendering = false;
        }
    }

    /// <summary>По текущей позиции трека вычисляет значения параметров, которые зависят от звука.</summary>
    private TransformerVariable[] BuildVariables(AudioTrackAnalysis analysis, TimeSpan position)
    {
        var result = new List<TransformerVariable>();

        foreach (var row in Mappings)
        {
            if (row.SelectedSource == AudioFeature.None)
            {
                continue;
            }

            double level = Smooth(row.SelectedSource, analysis.GetLevel(row.SelectedSource, position));
            double value = new AudioReactiveMapping(row.Variable, row.SelectedSource).Map(level);

            result.Add(row.Variable.WithValue(value));
            row.CurrentValueText = string.Format(_localizer.Get("music.value_format"), value, level);
        }

        return result.ToArray();
    }

    private double Smooth(AudioFeature feature, double target)
    {
        _smoothed.TryGetValue(feature, out double current);
        double factor = target > current ? AttackFactor : ReleaseFactor;
        current += (target - current) * factor;
        _smoothed[feature] = current;
        return current;
    }

    /// <summary>Вызывается при закрытии окна: музыка не должна играть дальше.</summary>
    public void Shutdown()
    {
        _analysisCts?.Cancel();
        _timer.Stop();
        _player.Stop();
        _player.Close();
    }
}
