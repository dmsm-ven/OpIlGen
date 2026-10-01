namespace OpIlGen.Services;

public interface IShellService
{
    /// <summary>Открывает папку в Проводнике (создаёт её, если не существует).</summary>
    void OpenFolder(string path);
}
