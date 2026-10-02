using Microsoft.Win32;

namespace OpIlGen.Services;

public interface IFileDialogService
{
    /// <summary>Показывает диалог выбора изображения. Возвращает путь или null.</summary>
    string? PickImageFile();
}

public sealed class FileDialogService : IFileDialogService
{
    public string? PickImageFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите изображение",
            Filter = "Изображения (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
