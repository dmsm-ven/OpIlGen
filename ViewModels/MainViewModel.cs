using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpIlGen.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;

namespace OpIlGen.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialog;
    private readonly IImageService _imageService;

    public MainViewModel(
        IFileDialogService fileDialog,
        IImageService imageService,
        IEnumerable<IImageTransformer> transformers)
    {
        _fileDialog = fileDialog;
        _imageService = imageService;

        Transformers = new ObservableCollection<IImageTransformer>(transformers);
        SelectedTransformer = Transformers.FirstOrDefault();
    }

    public ObservableCollection<IImageTransformer> Transformers { get; }

    [ObservableProperty]
    private string? _sourcePath;

    [ObservableProperty]
    private IImageTransformer? _selectedTransformer;

    [ObservableProperty]
    private BitmapSource? _resultImage;

    [ObservableProperty]
    private string _status = "Выберите изображение (.jpg / .png)";

    partial void OnSourcePathChanged(string? value) => ApplyTransform();

    partial void OnSelectedTransformerChanged(IImageTransformer? value) => ApplyTransform();

    [RelayCommand]
    private void SelectFile()
    {
        var path = _fileDialog.PickImageFile();
        if (path is not null)
        {
            SourcePath = path;
        }
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
            Status = $"Исходный файл: {Path.GetFileName(SourcePath)} | Результат сохранён: {savedPath}";
        }
        catch (Exception ex)
        {
            Status = $"Ошибка: {ex.Message}";
        }
    }
}
