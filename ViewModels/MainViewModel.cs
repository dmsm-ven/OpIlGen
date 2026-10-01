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

    public MainViewModel(
        IFileDialogService fileDialog,
        IImageService imageService,
        IFullScreenService fullScreen,
        IEnumerable<IImageTransformer> transformers)
    {
        _fileDialog = fileDialog;
        _imageService = imageService;
        _fullScreen = fullScreen;

        Transformers = new ObservableCollection<IImageTransformer>(transformers);
        SelectedTransformer = Transformers.FirstOrDefault();
    }

    public ObservableCollection<IImageTransformer> Transformers { get; }

    [ObservableProperty]
    private string? _sourcePath;

    [ObservableProperty]
    private IImageTransformer? _selectedTransformer;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFullScreenCommand))]
    private BitmapSource? _resultImage;

    /// <summary>Разрешение результата, например "1920x1080". Пусто, пока изображения нет.</summary>
    [ObservableProperty]
    private string _imageResolution = string.Empty;

    /// <summary>Статус выводится в заголовок окна.</summary>
    [ObservableProperty]
    private string _windowTitle = $"{AppName} - выберите изображение (.jpg / .png)";

    partial void OnSourcePathChanged(string? value) => ApplyTransform();

    partial void OnSelectedTransformerChanged(IImageTransformer? value) => ApplyTransform();

    partial void OnResultImageChanged(BitmapSource? value) =>
        ImageResolution = value is null ? string.Empty : $"{value.PixelWidth}x{value.PixelHeight}";

    [RelayCommand]
    private void SelectFile()
    {
        var path = _fileDialog.PickImageFile();
        if (path is not null)
        {
            SourcePath = path;
        }
    }

    [RelayCommand(CanExecute = nameof(HasResult))]
    private void OpenFullScreen()
    {
        if (ResultImage is not null)
        {
            _fullScreen.Show(ResultImage);
        }
    }

    private bool HasResult() => ResultImage is not null;

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
            WindowTitle = $"{AppName} - {Path.GetFileName(SourcePath)} -> {savedPath}";
        }
        catch (Exception ex)
        {
            WindowTitle = $"{AppName} - ошибка: {ex.Message}";
        }
    }
}
