using Microsoft.Extensions.DependencyInjection;
using OpIlGen.ViewModels;
using OpIlGen.Views;
using System.Windows;

namespace OpIlGen.Services;

public interface IMusicWindowService
{
    /// <summary>Открывает (модально) окно музыкального режима для выбранного файла и преобразователя.</summary>
    void Show(string sourcePath, IImageTransformer transformer);
}

public sealed class MusicWindowService : IMusicWindowService
{
    private readonly IServiceProvider _services;

    public MusicWindowService(IServiceProvider services)
    {
        _services = services;
    }

    public void Show(string sourcePath, IImageTransformer transformer)
    {
        // Зависимости ViewModel берутся из DI, а путь и преобразователь передаются явно
        var viewModel = ActivatorUtilities.CreateInstance<MusicViewModel>(_services, sourcePath, transformer);

        var window = new MusicWindow(viewModel)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }
}
