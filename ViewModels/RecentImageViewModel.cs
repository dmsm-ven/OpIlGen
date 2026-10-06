using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace OpIlGen.ViewModels;

/// <summary>Плитка недавнего изображения в верхней панели главного окна.</summary>
public partial class RecentImageViewModel : ObservableObject
{
    public RecentImageViewModel(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        ToolTipText = $"{System.IO.Path.GetFileName(path)}{Environment.NewLine}{path}";
    }

    public string Path { get; }

    public string Name { get; }

    public string ToolTipText { get; }

    /// <summary>Изображение выбрано сейчас (всегда первая плитка).</summary>
    [ObservableProperty]
    private bool _isCurrent;
}
