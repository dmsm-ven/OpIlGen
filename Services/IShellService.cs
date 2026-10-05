using System.Diagnostics;
using System.IO;

namespace OpIlGen.Services;

public interface IShellService
{
    /// <summary>Открывает папку в Проводнике (создаёт её, если не существует).</summary>
    void OpenFolder(string path);

    /// <summary>Открывает файл в программе по умолчанию (если её нет - в Блокноте).</summary>
    void OpenFile(string path);
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

    public void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Для .json не назначена программа: открываем в Блокноте
            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
    }
}
