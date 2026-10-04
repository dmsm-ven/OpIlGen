using Microsoft.Win32;
using OpIlGen.Localization;

namespace OpIlGen.Services;

public interface IFileDialogService
{
    /// <summary>Показывает диалог выбора изображения. Возвращает путь или null.</summary>
    string? PickImageFile();

    /// <summary>Показывает диалог выбора аудиофайла. Возвращает путь или null.</summary>
    string? PickAudioFile();
}

public sealed class FileDialogService : IFileDialogService
{
    private readonly ILocalizationService _localizer;

    public FileDialogService(ILocalizationService localizer)
    {
        _localizer = localizer;
    }

    public string? PickImageFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = _localizer.Get("dialog.select_image.title"),
            Filter = _localizer.Get("dialog.select_image.filter")
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickAudioFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = _localizer.Get("dialog.select_audio.title"),
            Filter = _localizer.Get("dialog.select_audio.filter")
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
