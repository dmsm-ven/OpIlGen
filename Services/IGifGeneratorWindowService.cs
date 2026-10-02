using Microsoft.Extensions.DependencyInjection;
using OpIlGen.ViewModels;
using OpIlGen.Views;
using System.Windows;

namespace OpIlGen.Services;

public interface IGifGeneratorWindowService
{
    /// <summary>Открывает (модально) окно генератора GIF для выбранного файла и преобразователя.</summary>
    void Show(string sourcePath, IImageTransformer transformer);
}

public sealed class GifGeneratorWindowService : IGifGeneratorWindowService
{
    private readonly IServiceProvider _services;

    public GifGeneratorWindowService(IServiceProvider services)
    {
        _services = services;
    }

    public void Show(string sourcePath, IImageTransformer transformer)
    {
        // Зависимости ViewModel берутся из DI, а путь и преобразователь передаются явно
        var viewModel = ActivatorUtilities.CreateInstance<GifGeneratorViewModel>(_services, sourcePath, transformer);

        var window = new GifGeneratorWindow(viewModel)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }
}
