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
    private readonly IThemeService _themeService;
    private readonly ISettingsService _settings;

    public MainViewModel(
        IFileDialogService fileDialog,
        IImageService imageService,
        IFullScreenService fullScreen,
        IShellService shell,
        IThemeService themeService,
        ISettingsService settings,
        IEnumerable<IImageTransformer> transformers)
    {
        _fileDialog = fileDialog;
        _imageService = imageService;
        _fullScreen = fullScreen;
        _shell = shell;
        _themeService = themeService;
        _settings = settings;
        IsDarkTheme = themeService.Current == AppTheme.Dark;

        Transformers = new ObservableCollection<IImageTransformer>(transformers);
        SelectedTransformer = Transformers.FirstOrDefault();
    }

    public ObservableCollection<IImageTransformer> Transformers { get; }

    [ObservableProperty]
    private string? _sourcePath;

    [ObservableProperty]
    private IImageTransformer? _selectedTransformer;

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

    partial void OnSourcePathChanged(string? value) => ApplyTransform();

    partial void OnSelectedTransformerChanged(IImageTransformer? value) => ApplyTransform();

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

    private void ApplyTransform()
    {
        if (string.IsNullOrEmpty(SourcePath) || SelectedTransformer is null)
        {
            return;
        }

        try
        {
            var source = _imageService.Load(SourcePath);
            var result = SelectedTransformer.Transform(source);
            var savedPath = _imageService.SaveResult(result, SourcePath, SelectedTransformer.Key);

            ResultImage = result;
            Title = $"{AppName} - {Path.GetFileName(SourcePath)} -> {savedPath}";
        }
        catch (Exception ex)
        {
            Title = $"{AppName} - ошибка: {ex.Message}";
        }
    }
}
