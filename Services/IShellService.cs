using System.Diagnostics;
using System.IO;

namespace OpIlGen.Services;

public interface IShellService
{
    /// <summary>Открывает папку в Проводнике (создаёт её, если не существует).</summary>
    void OpenFolder(string path);
}

public sealed class ShellService : IShellService
{
    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
