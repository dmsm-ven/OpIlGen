using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpIlGen.Localization;
using OpIlGen.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace OpIlGen.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string AppName = "OpIlGen";
    private const int MaxRecentImages = 8;

    private readonly IFileDialogService _fileDialog;
    private readonly IImageService _imageService;
    private readonly IFullScreenService _fullScreen;
    private readonly IShellService _shell;
    private readonly IGifGeneratorWindowService _gifGenerator;
    private readonly IMusicWindowService _musicWindow;
    private readonly ISettingsWindowService _settingsWindow;
    private readonly ILocalizationService _localizer;
    private readonly ISettingsService _settings;
    private ICollectionView? _transformersView;

    // Значения параметров запоминаются для каждого преобразователя при переключении между ними
    private readonly Dictionary<string, TransformerVariableViewModel[]> _variableCache = new();

    /// <summary>Через сколько после последнего изменения результат сохраняется в файл.</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2.5);

    // Исходное изображение кэшируется, чтобы не читать файл при каждом изменении эффекта
    private BitmapSource? _loadedImage;
    private string? _loadedPath;

    private CancellationTokenSource? _transformCts;
    private CancellationTokenSource? _saveCts;
    private (BitmapSource Image, string SourcePath, string Key)? _pendingSave;

    // Текущий статус хранится как функция, чтобы заголовок окна пересобирался при смене языка
    private Func<string> _statusFactory = () => string.Empty;

    public MainViewModel(
        IFileDialogService fileDialog,
        IImageService imageService,
        IFullScreenService fullScreen,
        IShellService shell,
        IGifGeneratorWindowService gifGenerator,
        IMusicWindowService musicWindow,
        ISettingsWindowService settingsWindow,
        ISettingsService settings,
        ILocalizationService localizer,
        IEnumerable<IImageTransformer> transformers)
    {
        _fileDialog = fileDialog;
        _imageService = imageService;
        _fullScreen = fullScreen;
        _shell = shell;
        _gifGenerator = gifGenerator;
        _musicWindow = musicWindow;
        _settingsWindow = settingsWindow;
        _localizer = localizer;
        _settings = settings;

        _localizer.LanguageChanged += OnLanguageChanged;

        var favorites = new HashSet<string>(_settings.Current.FavoriteTransformers);
        Transformers = new ObservableCollection<TransformerItemViewModel>(
            transformers.Select(t => new TransformerItemViewModel(t, localizer, favorites.Contains(t.Key), OnFavoriteChanged)));
        ConfigureTransformersSorting();

        RecentImages = new ObservableCollection<RecentImageViewModel>(
            _settings.Current.RecentImages
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRecentImages)
                .Select(path => new RecentImageViewModel(path)));
        RecentImages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoRecentImages));

        SetStatus(() => _localizer.Get("status.select_image"));
        // Первый в отсортированном списке (избранные, затем по алфавиту), а не в порядке регистрации
        SelectedTransformer = _transformersView!.Cast<TransformerItemViewModel>().FirstOrDefault()?.Transformer;
    }

    public ObservableCollection<TransformerItemViewModel> Transformers { get; }

    /// <summary>Недавние изображения (первое - выбранное сейчас).</summary>
    public ObservableCollection<RecentImageViewModel> RecentImages { get; }

    public bool HasNoRecentImages => RecentImages.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenGifGeneratorCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenMusicWindowCommand))]
    private string? _sourcePath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenGifGeneratorCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenMusicWindowCommand))]
    [NotifyPropertyChangedFor(nameof(SelectedDescription))]
    private IImageTransformer? _selectedTransformer;

    /// <summary>Описание выбранного преобразователя на текущем языке.</summary>
    public string SelectedDescription => SelectedTransformer?.Description ?? string.Empty;

    /// <summary>Настраиваемые параметры выбранного преобразователя (ползунки в UI).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomVariables))]
    private IReadOnlyList<TransformerVariableViewModel> _customVariables = Array.Empty<TransformerVariableViewModel>();

    public bool HasCustomVariables => CustomVariables.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowFullScreenCommand))]
    private BitmapSource? _resultImage;

    /// <summary>Разрешение результата, например "1920×1080". Выводится поверх холста.</summary>
    [ObservableProperty]
    private string _resolutionText = string.Empty;

    /// <summary>Заголовок окна, он же строка статуса.</summary>
    [ObservableProperty]
    private string _title = AppName;

    partial void OnSourcePathChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            RegisterRecentImage(value);
        }

        RequestTransform();
    }

    partial void OnSelectedTransformerChanged(IImageTransformer? value)
    {
        CustomVariables = GetVariables(value);
        RequestTransform();
    }

    partial void OnResultImageChanged(BitmapSource? value)
    {
        ResolutionText = value is null ? string.Empty : $"{value.PixelWidth}×{value.PixelHeight}";
    }

    // ---- Статус в заголовке окна ----

    private void SetStatus(Func<string> factory)
    {
        _statusFactory = factory;
        Title = ComposeTitle();
    }

    private string ComposeTitle() => $"{AppName} - {_statusFactory()}";

    /// <summary>Избранные сверху, внутри групп по алфавиту. Порядок обновляется сам при смене отметки или языка.</summary>
    private void ConfigureTransformersSorting()
    {
        var view = CollectionViewSource.GetDefaultView(Transformers);
        UpdateSortCulture(view);

        view.SortDescriptions.Add(new SortDescription(nameof(TransformerItemViewModel.IsFavorite), ListSortDirection.Descending));
        view.SortDescriptions.Add(new SortDescription(nameof(TransformerItemViewModel.Name), ListSortDirection.Ascending));

        if (view is ICollectionViewLiveShaping live && live.CanChangeLiveSorting)
        {
            live.LiveSortingProperties.Add(nameof(TransformerItemViewModel.IsFavorite));
            live.LiveSortingProperties.Add(nameof(TransformerItemViewModel.Name));
            live.IsLiveSorting = true;
        }

        _transformersView = view;
    }

    /// <summary>Алфавит зависит от языка интерфейса: названия сравниваются по правилам его культуры.</summary>
    private void UpdateSortCulture(ICollectionView view)
    {
        try
        {
            view.Culture = new CultureInfo(_localizer.CurrentLanguage.Code);
        }
        catch (CultureNotFoundException)
        {
            view.Culture = CultureInfo.InvariantCulture;
        }
    }

    private void OnFavoriteChanged(TransformerItemViewModel item)
    {
        var favorites = _settings.Current.FavoriteTransformers;
        favorites.RemoveAll(key => key == item.Transformer.Key);
        if (item.IsFavorite)
        {
            favorites.Add(item.Transformer.Key);
        }

        _settings.Save();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_transformersView is not null)
        {
            UpdateSortCulture(_transformersView);
        }

        Title = ComposeTitle();
        OnPropertyChanged(nameof(SelectedDescription));
    }

    // ---- Команды ----

    [RelayCommand]
    private void SelectFile()
    {
        var path = _fileDialog.PickImageFile();
        if (path is not null)
        {
            SourcePath = path;
        }
    }

    /// <summary>Клик по плитке недавнего изображения: оно становится текущим, как при выборе через «Выбрать файл...».</summary>
    [RelayCommand]
    private void SelectRecent(RecentImageViewModel? image)
    {
        if (image is null || image.IsCurrent)
        {
            return;
        }

        if (!File.Exists(image.Path))
        {
            // Файл удалён или недоступен: убираем плитку и сообщаем
            RecentImages.Remove(image);
            UpdateCurrentRecent();
            SaveRecentImages();
            var path = image.Path;
            SetStatus(() => _localizer.Format("status.image_missing", path));
            return;
        }

        SourcePath = image.Path;
    }

    /// <summary>Ставит изображение первым в списке недавних (без дублей), лишние старые отбрасывает.</summary>
    private void RegisterRecentImage(string path)
    {
        var existing = RecentImages.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            RecentImages.Move(RecentImages.IndexOf(existing), 0);
        }
        else
        {
            RecentImages.Insert(0, new RecentImageViewModel(path));
        }

        while (RecentImages.Count > MaxRecentImages)
        {
            RecentImages.RemoveAt(RecentImages.Count - 1);
        }

        UpdateCurrentRecent();
        SaveRecentImages();
    }

    private void UpdateCurrentRecent()
    {
        for (int i = 0; i < RecentImages.Count; i++)
        {
            RecentImages[i].IsCurrent = i == 0 && !string.IsNullOrEmpty(SourcePath);
        }
    }

    private void SaveRecentImages()
    {
        _settings.Current.RecentImages = RecentImages.Select(i => i.Path).ToList();
        _settings.Save();
    }

    [RelayCommand(CanExecute = nameof(CanShowFullScreen))]
    private void ShowFullScreen()
    {
        if (ResultImage is not null)
        {
            _fullScreen.Show(ResultImage);
        }
    }

    private bool CanShowFullScreen() => ResultImage is not null;

    [RelayCommand(CanExecute = nameof(CanOpenGifGenerator))]
    private void OpenGifGenerator()
    {
        if (SourcePath is not null && SelectedTransformer is not null)
        {
            // Окно модальное: берётся выбор на момент открытия (файл и преобразователь)
            _gifGenerator.Show(SourcePath, SelectedTransformer);
        }
    }

    private bool CanOpenGifGenerator() => !string.IsNullOrEmpty(SourcePath) && SelectedTransformer is not null;

    [RelayCommand(CanExecute = nameof(CanOpenMusicWindow))]
    private void OpenMusicWindow()
    {
        if (SourcePath is not null && SelectedTransformer is not null)
        {
            // Окно модальное: берётся выбор на момент открытия (файл и преобразователь)
            _musicWindow.Show(SourcePath, SelectedTransformer);
        }
    }

    private bool CanOpenMusicWindow() => !string.IsNullOrEmpty(SourcePath) && SelectedTransformer is not null;

    [RelayCommand]
    private void OpenOutputFolder()
    {
        try
        {
            _shell.OpenFolder(_imageService.OutputDirectory);
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            SetStatus(() => _localizer.Format("status.open_folder_failed", message));
        }
    }

    [RelayCommand]
    private void OpenSettings() => _settingsWindow.Show();

    // ---- Параметры преобразователя ----

    private IReadOnlyList<TransformerVariableViewModel> GetVariables(IImageTransformer? transformer)
    {
        if (transformer is null)
        {
            return Array.Empty<TransformerVariableViewModel>();
        }

        if (!_variableCache.TryGetValue(transformer.Key, out var variables))
        {
            variables = transformer.AvailableCustomVariables
                .Select(v => new TransformerVariableViewModel(v, RequestTransform, _localizer))
                .ToArray();
            _variableCache[transformer.Key] = variables;
        }

        return variables;
    }

    // ---- Преобразование и сохранение ----

    private void RequestTransform() => _ = ApplyTransformAsync();

    /// <summary>
    /// Преобразование выполняется в фоне, результат сразу выводится на холст,
    /// а сохранение в файл откладывается (см. <see cref="SaveAfterDelayAsync"/>).
    /// </summary>
    private async Task ApplyTransformAsync()
    {
        if (string.IsNullOrEmpty(SourcePath) || SelectedTransformer is null)
        {
            return;
        }

        // Предыдущее, ещё не завершённое преобразование становится неактуальным
        _transformCts?.Cancel();
        var cts = new CancellationTokenSource();
        _transformCts = cts;

        var path = SourcePath;
        var transformer = SelectedTransformer;
        var variables = CustomVariables.Select(v => v.ToModel()).ToArray();
        var cachedSource = path == _loadedPath ? _loadedImage : null;

        try
        {
            var (source, result) = await Task.Run(() =>
            {
                var src = cachedSource ?? _imageService.Load(path);
                return (src, transformer.Transform(src, variables));
            });

            if (cts.IsCancellationRequested)
            {
                return; // за время расчёта пользователь уже изменил настройки
            }

            _loadedPath = path;
            _loadedImage = source;

            ResultImage = result;

            var fileName = Path.GetFileName(path);
            SetStatus(() => _localizer.Format("status.saving", fileName));

            _ = SaveAfterDelayAsync(result, path, transformer.Key);
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
            {
                var message = ex.Message;
                SetStatus(() => _localizer.Format("status.error", message));
            }
        }
    }

    /// <summary>Сохраняет результат в файл, если за <see cref="SaveDelay"/> не было новых изменений.</summary>
    private async Task SaveAfterDelayAsync(BitmapSource image, string sourcePath, string key)
    {
        _saveCts?.Cancel();
        var cts = new CancellationTokenSource();
        _saveCts = cts;
        _pendingSave = (image, sourcePath, key);

        try
        {
            await Task.Delay(SaveDelay, cts.Token);
            _pendingSave = null;

            var savedPath = await Task.Run(() => _imageService.SaveResult(image, sourcePath, key));

            if (!cts.IsCancellationRequested)
            {
                var fileName = Path.GetFileName(sourcePath);
                SetStatus(() => $"{fileName} -> {savedPath}");
            }
        }
        catch (OperationCanceledException)
        {
            // Появилось более свежее изменение - сохранится оно
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            SetStatus(() => _localizer.Format("status.save_error", message));
        }
    }

    /// <summary>Немедленно сохраняет результат, если сохранение ещё ожидает (вызывается при закрытии окна).</summary>
    public void FlushPendingSave()
    {
        _saveCts?.Cancel();

        if (_pendingSave is { } pending)
        {
            _pendingSave = null;
            try
            {
                _imageService.SaveResult(pending.Image, pending.SourcePath, pending.Key);
            }
            catch
            {
                // Закрываем окно - ошибку сохранения показывать уже некому
            }
        }
    }
}
