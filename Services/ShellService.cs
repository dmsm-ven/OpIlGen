using System.Diagnostics;
using System.IO;

namespace OpIlGen.Services;

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
