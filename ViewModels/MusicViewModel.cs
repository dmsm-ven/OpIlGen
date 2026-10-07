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

    /// <summary>В полноэкранном режиме картинка считается крупнее, иначе на большом экране она была бы мыльной.</summary>
    private const int FullScreenPreviewMaxSide = 1280;

    /// <summary>Допустимые значения FPS (число обновлений изображения в секунду).</summary>
    public const double MinFps = 1;
    public const double MaxFps = 30;
    private const double DefaultFps = 10;

    /// <summary>Сколько недавних треков помним: при переполнении вытесняется самый старый.</summary>
    private const int MaxRecentTracks = 8;

    /// <summary>
    /// Коэффициенты сглаживания ниже подобраны для этого FPS. При другом FPS они пересчитываются,
    /// чтобы скорость реакции по времени оставалась той же.
    /// </summary>
    private const double SmoothingReferenceFps = 10;

    /// <summary>Сглаживание: доля пути к новому уровню за один шаг (быстро растёт, медленнее спадает).</summary>
    private const double AttackFactor = 0.7;
    private const double ReleaseFactor = 0.3;

    /// <summary>Ритм короткий и резкий: ему нужна почти мгновенная реакция и быстрый спад.</summary>
    private const double BeatAttackFactor = 0.95;
    private const double BeatReleaseFactor = 0.5;

    /// <summary>Громкость плеера: ползунок 0..1 -> от -60 дБ до 0 дБ (логарифмическая шкала).</summary>
    private const double VolumeRangeDb = 60;
    private const double DefaultVolumePosition = 0.9;

    private readonly IFileDialogService _fileDialog;
    private readonly IFullScreenService _fullScreen;
    private readonly IImageService _imageService;
    private readonly IAudioAnalysisService _analysisService;
    private readonly ILocalizationService _localizer;
    private readonly ISettingsService _settings;
    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<AudioFeature, double> _smoothed = new();

    private BitmapSource? _baseImage;
    private BitmapSource? _sourceImage;
    private bool _isFullScreenOpen;
    private CancellationTokenSource? _analysisCts;
    private bool _isRendering;
    private bool _previewDirty;
    private bool _isSeeking;
    private bool _isUpdatingPosition;

    public MusicViewModel(
        string sourcePath,
        IImageTransformer transformer,
        IFileDialogService fileDialog,
        IFullScreenService fullScreen,
        IImageService imageService,
        IAudioAnalysisService analysisService,
        ISettingsService settings,
        ILocalizationService localizer)
    {
        SourcePath = sourcePath;
        Transformer = transformer;
        _fileDialog = fileDialog;
        _fullScreen = fullScreen;
        _imageService = imageService;
        _analysisService = analysisService;
        _localizer = localizer;
        _settings = settings;

        // Сохранённые FPS и громкость (задаём поля напрямую: таймера и плеера ещё нет в нужном состоянии)
        _fps = Math.Clamp(Math.Round(settings.Current.MusicFps), MinFps, MaxFps);
        _volumePosition = Math.Clamp(settings.Current.MusicVolumePosition, 0, 1);

        _status = localizer.Get("music.status.initial");

        var options = Enum.GetValues<AudioFeature>()
            .Select(f => new AudioSourceOption(f, localizer.Get($"music.source.{f.ToString().ToLowerInvariant()}")))
            .ToArray();
        Mappings = new ObservableCollection<MusicMappingRowViewModel>(
            transformer.AvailableCustomVariables.Select(v => new MusicMappingRowViewModel(v, localizer, options)));
        RestoreMappings();

        RecentTracks = new ObservableCollection<RecentTrackViewModel>(
            _settings.Current.MusicRecentTracks
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRecentTracks)
                .Select(path => new RecentTrackViewModel(path)));
        UpdateCurrentRecent();
        RecentTracks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoRecentTracks));
        foreach (var row in Mappings)
        {
            row.FixedStateChanged += (_, _) => RefreshPreview();
        }

        _player.Volume = PositionToGain(_volumePosition);

        _timer = new DispatcherTimer { Interval = FpsToInterval(Fps) };
        _timer.Tick += OnTimerTick;

        _player.MediaOpened += (_, _) =>
        {
            if (_player.NaturalDuration.HasTimeSpan)
            {
                DurationSeconds = _player.NaturalDuration.TimeSpan.TotalSeconds;
            }
        };
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

    /// <summary>Недавние треки для верхней панели; первый - выбранный сейчас.</summary>
    public ObservableCollection<RecentTrackViewModel> RecentTracks { get; }

    public bool HasNoRecentTracks => RecentTracks.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowFullScreenCommand))]
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

    /// <summary>Положение ползунка громкости (0..1). Громкость растёт логарифмически, как в обычных плеерах.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeText))]
    private double _volumePosition = DefaultVolumePosition;

    public string VolumeText => VolumePosition <= 0
        ? _localizer.Get("music.volume.mute")
        : string.Format(_localizer.Get("music.volume.db_format"), -VolumeRangeDb * (1 - VolumePosition));

    partial void OnVolumePositionChanged(double value)
    {
        _player.Volume = PositionToGain(value);
        _settings.Current.MusicVolumePosition = value;
    }

    /// <summary>Положение ползунка (0..1) -> множитель громкости: 0 = тишина, иначе от -60 дБ до 0 дБ.</summary>
    private static double PositionToGain(double position)
        => position <= 0 ? 0 : Math.Pow(10, -VolumeRangeDb * (1 - Math.Min(position, 1)) / 20);

    /// <summary>Сколько раз в секунду обновляется изображение.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FpsText))]
    private double _fps = DefaultFps;

    public string FpsText => string.Format("{0:0}", Fps);

    partial void OnFpsChanged(double value)
    {
        _timer.Interval = FpsToInterval(value);
        _settings.Current.MusicFps = value;
    }

    private static TimeSpan FpsToInterval(double fps)
        => TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(Math.Round(fps), MinFps, MaxFps));

    /// <summary>Текущая позиция трека в секундах (ползунок позиции).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    private double _positionSeconds;

    /// <summary>Длительность трека в секундах (максимум ползунка позиции).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    private double _durationSeconds;

    public string PositionText => $"{FormatTime(PositionSeconds)} / {FormatTime(DurationSeconds)}";

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    partial void OnPositionSecondsChanged(double value)
    {
        // Значение обновил таймер или идёт перетаскивание (позиция применится в конце) - плеер не трогаем
        if (_isUpdatingPosition || _isSeeking || !IsPlaying)
        {
            return;
        }

        _player.Position = TimeSpan.FromSeconds(value);
    }

    /// <summary>Пользователь начал тянуть ползунок позиции: таймер временно не двигает его.</summary>
    public void BeginSeek() => _isSeeking = true;

    /// <summary>Пользователь отпустил ползунок позиции: перематываем трек.</summary>
    public void EndSeek()
    {
        _isSeeking = false;
        if (IsPlaying)
        {
            _player.Position = TimeSpan.FromSeconds(PositionSeconds);
        }
    }

    private void SetPositionFromPlayer(double seconds)
    {
        _isUpdatingPosition = true;
        PositionSeconds = seconds;
        _isUpdatingPosition = false;
    }

    public string MusicName => string.IsNullOrEmpty(MusicPath) ? "-" : Path.GetFileName(MusicPath);

    private void LoadBaseImage()
    {
        try
        {
            _sourceImage = _imageService.Load(SourcePath);
            _baseImage = CreatePreview(_sourceImage, PreviewMaxSide);
            ResultImage = Transformer.Transform(_baseImage, BuildFixedVariables());
        }
        catch (Exception ex)
        {
            Status = string.Format(_localizer.Get("status.error"), ex.Message);
        }
    }

    private static BitmapSource CreatePreview(BitmapSource source, int maxSide)
    {
        double longSide = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longSide <= maxSide)
        {
            return source;
        }

        double scale = maxSide / longSide;
        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    /// <summary>Двойной клик по холсту: изображение на весь экран. Оно обновляется вместе с музыкой.</summary>
    [RelayCommand(CanExecute = nameof(CanShowFullScreen))]
    private void ShowFullScreen()
    {
        if (_isFullScreenOpen || ResultImage is null)
        {
            return;
        }

        _isFullScreenOpen = true;
        ApplyPreviewSize(FullScreenPreviewMaxSide);

        _fullScreen.ShowLive(
            this,
            nameof(ResultImage),
            () => ResultImage,
            () =>
            {
                _isFullScreenOpen = false;
                ApplyPreviewSize(PreviewMaxSide);
            });
    }

    private bool CanShowFullScreen() => ResultImage is not null;

    /// <summary>Меняет размер, в котором считается картинка. Следующий кадр уже будет с новым размером.</summary>
    private void ApplyPreviewSize(int maxSide)
    {
        if (_sourceImage is null)
        {
            return;
        }

        _baseImage = CreatePreview(_sourceImage, maxSide);
        RefreshPreview();
    }

    [RelayCommand]
    private async Task SelectMusicAsync()
    {
        var path = _fileDialog.PickAudioFile();
        if (path is null)
        {
            return;
        }

        // Выбор через «Выбрать файл...»: трек становится первым в списке недавних
        await LoadTrackAsync(path, promoteToFront: true);
    }

    /// <summary>Клик по плитке недавнего трека: он становится текущим (выделен), но остаётся на своём месте в списке.</summary>
    [RelayCommand]
    private async Task SelectRecentAsync(RecentTrackViewModel? track)
    {
        if (track is null || track.IsCurrent)
        {
            return;
        }

        if (!File.Exists(track.Path))
        {
            // Файл удалён или недоступен: убираем плитку и сообщаем
            RecentTracks.Remove(track);
            UpdateCurrentRecent();
            Status = string.Format(_localizer.Get("music.status.analysis_error"), track.Path);
            return;
        }

        await LoadTrackAsync(track.Path);
    }

    /// <summary>
    /// Новый трек ставится первым в списке недавних, лишние старые отбрасываются. Уже известный трек
    /// переезжает на первое место только при <paramref name="promoteToFront"/> (выбор через «Выбрать файл...»),
    /// при клике по плитке порядок не меняется.
    /// </summary>
    private void RegisterRecent(string path, bool promoteToFront)
    {
        var existing = RecentTracks.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            RecentTracks.Insert(0, new RecentTrackViewModel(path));
        }
        else if (promoteToFront)
        {
            RecentTracks.Move(RecentTracks.IndexOf(existing), 0);
        }

        while (RecentTracks.Count > MaxRecentTracks)
        {
            RecentTracks.RemoveAt(RecentTracks.Count - 1);
        }

        UpdateCurrentRecent();
    }

    private void UpdateCurrentRecent()
    {
        for (int i = 0; i < RecentTracks.Count; i++)
        {
            RecentTracks[i].IsCurrent = !string.IsNullOrEmpty(MusicPath)
                && string.Equals(RecentTracks[i].Path, MusicPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Открывает последний выбранный трек (если файл ещё существует). Вызывается при открытии окна.</summary>
    public async Task InitializeAsync()
    {
        var path = _settings.Current.MusicLastTrack;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            await LoadTrackAsync(path);
        }
    }

    private async Task LoadTrackAsync(string path, bool promoteToFront = false)
    {
        StopInternal();
        _analysisCts?.Cancel();
        var cts = _analysisCts = new CancellationTokenSource();

        MusicPath = path;
        RegisterRecent(path, promoteToFront);
        Analysis = null;
        IsAnalyzing = true;
        Status = _localizer.Get("music.status.analyzing");

        try
        {
            var result = await _analysisService.AnalyzeAsync(path, cts.Token);
            if (result is null || cts.IsCancellationRequested)
            {
                return;
            }

            Analysis = result;
            _settings.Current.MusicLastTrack = path;
            DurationSeconds = result.Duration.TotalSeconds;
            SetPositionFromPlayer(0);
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
                // Нечитаемый файл в недавних не оставляем
                var broken = RecentTracks.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
                if (broken is not null)
                {
                    RecentTracks.Remove(broken);
                    UpdateCurrentRecent();
                }

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
        _player.Volume = PositionToGain(VolumePosition);
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
        _isSeeking = false;
        SetPositionFromPlayer(0);

        // Возвращаем картинку к значениям по умолчанию
        if (_baseImage is not null)
        {
            ResultImage = Transformer.Transform(_baseImage, BuildFixedVariables());
        }

        foreach (var row in Mappings)
        {
            row.CurrentValueText = string.Empty;
        }
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        // Ползунок позиции двигается на каждом тике, даже если кадр изображения пропущен
        if (IsPlaying && !_isSeeking)
        {
            SetPositionFromPlayer(_player.Position.TotalSeconds);
        }

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

            if (row.SelectedSource == AudioFeature.Fixed)
            {
                result.Add(row.Variable.WithValue(row.FixedValue));
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
        bool isBeat = feature == AudioFeature.Beat;
        double factor = target > current
            ? (isBeat ? BeatAttackFactor : AttackFactor)
            : (isBeat ? BeatReleaseFactor : ReleaseFactor);
        factor = 1 - Math.Pow(1 - factor, SmoothingReferenceFps / Math.Clamp(Math.Round(Fps), MinFps, MaxFps));
        current += (target - current) * factor;
        _smoothed[feature] = current;
        return current;
    }

    /// <summary>Параметры, замороженные на введённом вручную значении.</summary>
    private TransformerVariable[] BuildFixedVariables()
        => Mappings
            .Where(r => r.SelectedSource == AudioFeature.Fixed)
            .Select(r => r.Variable.WithValue(r.FixedValue))
            .ToArray();

    /// <summary>
    /// Перерисовывает картинку, пока музыка не играет (во время воспроизведения кадры рисует таймер).
    /// Если значения меняются быстрее, чем считается кадр, после него считается ещё один, уже с последними.
    /// </summary>
    private async void RefreshPreview()
    {
        if (IsPlaying || _baseImage is null)
        {
            return;
        }

        _previewDirty = true;
        if (_isRendering)
        {
            return;
        }

        _isRendering = true;
        try
        {
            while (_previewDirty && !IsPlaying)
            {
                _previewDirty = false;
                var variables = BuildFixedVariables();
                var source = _baseImage;
                var image = await Task.Run(() => Transformer.Transform(source, variables));

                if (!IsPlaying)
                {
                    ResultImage = image;
                }
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

    /// <summary>Возвращает привязки параметров к звуку, сохранённые для этого преобразователя (не зависит от изображения).</summary>
    private void RestoreMappings()
    {
        if (!_settings.Current.MusicMappings.TryGetValue(Transformer.Key, out var saved))
        {
            return;
        }

        _settings.Current.MusicFixedValues.TryGetValue(Transformer.Key, out var fixedValues);

        foreach (var row in Mappings)
        {
            if (fixedValues is not null && fixedValues.TryGetValue(row.Variable.Key, out var fixedValue))
            {
                row.FixedValue = fixedValue;
            }

            if (saved.TryGetValue(row.Variable.Key, out var name)
                && Enum.TryParse<AudioFeature>(name, out var feature)
                && Enum.IsDefined(feature))
            {
                row.SelectedSource = feature;
            }
        }
    }

    /// <summary>Запоминает привязки для этого преобразователя. Параметры без привязки не хранятся.</summary>
    private void SaveMappings()
    {
        var bound = Mappings
            .Where(r => r.SelectedSource != AudioFeature.None)
            .ToDictionary(r => r.Variable.Key, r => r.SelectedSource.ToString());

        var fixedValues = Mappings
            .Where(r => r.SelectedSource == AudioFeature.Fixed)
            .ToDictionary(r => r.Variable.Key, r => r.FixedValue);

        if (bound.Count > 0)
        {
            _settings.Current.MusicMappings[Transformer.Key] = bound;
        }
        else
        {
            _settings.Current.MusicMappings.Remove(Transformer.Key);
        }

        if (fixedValues.Count > 0)
        {
            _settings.Current.MusicFixedValues[Transformer.Key] = fixedValues;
        }
        else
        {
            _settings.Current.MusicFixedValues.Remove(Transformer.Key);
        }
    }

    /// <summary>Вызывается при закрытии окна: музыка не должна играть дальше.</summary>
    public void Shutdown()
    {
        // FPS, громкость и привязки пишем на диск один раз при закрытии, а не на каждое изменение
        SaveMappings();
        // В файл текущий трек пишем первым: при следующем запуске он стоит в начале списка
        _settings.Current.MusicRecentTracks = RecentTracks.OrderByDescending(t => t.IsCurrent).Select(t => t.Path).ToList();
        _settings.Save();

        _analysisCts?.Cancel();
        _timer.Stop();
        _player.Stop();
        _player.Close();
    }
}
