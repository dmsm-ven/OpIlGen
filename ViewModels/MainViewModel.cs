using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpIlGen.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;

namespace OpIlGen.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string AppName = "OpIlGen";

    private readonly IFileDialogService _fileDialog;
    private readonly IImageService _imageService;
    private readonly IFullScreenService _fullScreen;
    private readonly IShellService _shell;
    private readonly IGifGeneratorWindowService _gifGenerator;
    private readonly IThemeService _themeService;
    private readonly ISettingsService _settings;

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

    public MainViewModel(
        IFileDialogService fileDialog,
        IImageService imageService,
        IFullScreenService fullScreen,
        IShellService shell,
        IGifGeneratorWindowService gifGenerator,
        IThemeService themeService,
        ISettingsService settings,
        IEnumerable<IImageTransformer> transformers)
    {
        _fileDialog = fileDialog;
        _imageService = imageService;
        _fullScreen = fullScreen;
        _shell = shell;
        _gifGenerator = gifGenerator;
        _themeService = themeService;
        _settings = settings;
        IsDarkTheme = themeService.Current == AppTheme.Dark;

        Transformers = new ObservableCollection<IImageTransformer>(transformers);
        SelectedTransformer = Transformers.FirstOrDefault();
    }

    public ObservableCollection<IImageTransformer> Transformers { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenGifGeneratorCommand))]
    private string? _sourcePath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenGifGeneratorCommand))]
    private IImageTransformer? _selectedTransformer;

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

    /// <summary>Выбрана ли тёмная тема (для подсветки кнопки темы).</summary>
    [ObservableProperty]
    private bool _isDarkTheme;

    /// <summary>Заголовок окна, он же строка статуса.</summary>
    [ObservableProperty]
    private string _title = $"{AppName} - выберите изображение (.jpg / .png)";

    partial void OnSourcePathChanged(string? value) => RequestTransform();

    partial void OnSelectedTransformerChanged(IImageTransformer? value)
    {
        CustomVariables = GetVariables(value);
        RequestTransform();
    }

    partial void OnResultImageChanged(BitmapSource? value)
    {
        ResolutionText = value is null ? string.Empty : $"{value.PixelWidth}×{value.PixelHeight}";
    }

    [RelayCommand]
    private void SelectFile()
    {
        var path = _fileDialog.PickImageFile();
        if (path is not null)
        {
            SourcePath = path;
        }
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

    [RelayCommand]
    private void OpenOutputFolder()
    {
        try
        {
            _shell.OpenFolder(_imageService.OutputDirectory);
        }
        catch (Exception ex)
        {
            Title = $"{AppName} - не удалось открыть папку: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SetLightTheme() => ChangeTheme(AppTheme.Light);

    [RelayCommand]
    private void SetDarkTheme() => ChangeTheme(AppTheme.Dark);

    private void ChangeTheme(AppTheme theme)
    {
        _themeService.Apply(theme);
        IsDarkTheme = theme == AppTheme.Dark;

        _settings.Current.Theme = theme;
        _settings.Save();
    }

    private IReadOnlyList<TransformerVariableViewModel> GetVariables(IImageTransformer? transformer)
    {
        if (transformer is null)
        {
            return Array.Empty<TransformerVariableViewModel>();
        }

        if (!_variableCache.TryGetValue(transformer.Key, out var variables))
        {
            variables = transformer.AvailableCustomVariables
                .Select(v => new TransformerVariableViewModel(v, RequestTransform))
                .ToArray();
            _variableCache[transformer.Key] = variables;
        }

        return variables;
    }

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
            Title = $"{AppName} - {Path.GetFileName(path)} (сохранение...)";

            _ = SaveAfterDelayAsync(result, path, transformer.Key);
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
            {
                Title = $"{AppName} - ошибка: {ex.Message}";
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
                Title = $"{AppName} - {Path.GetFileName(sourcePath)} -> {savedPath}";
            }
        }
        catch (OperationCanceledException)
        {
            // Появилось более свежее изменение - сохранится оно
        }
        catch (Exception ex)
        {
            Title = $"{AppName} - ошибка сохранения: {ex.Message}";
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
