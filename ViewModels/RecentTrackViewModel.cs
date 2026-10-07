using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace OpIlGen.ViewModels;

/// <summary>Плитка недавнего трека в верхней панели окна «Музыкальный режим».</summary>
public partial class RecentTrackViewModel : ObservableObject
{
    public RecentTrackViewModel(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        ToolTipText = $"{System.IO.Path.GetFileName(path)}{Environment.NewLine}{path}";
    }

    public string Path { get; }

    public string Name { get; }

    public string ToolTipText { get; }

    /// <summary>Трек выбран сейчас (выделен синим). Не обязательно первая плитка.</summary>
    [ObservableProperty]
    private bool _isCurrent;
}
