namespace OpIlGen.Services;

public interface IFileDialogService
{
    /// <summary>Показывает диалог выбора изображения. Возвращает путь или null.</summary>
    string? PickImageFile();
}
