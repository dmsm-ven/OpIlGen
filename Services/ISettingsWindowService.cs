using Microsoft.Extensions.DependencyInjection;
using OpIlGen.Views;
using System.Windows;

namespace OpIlGen.Services;

public interface ISettingsWindowService
{
    /// <summary>Открывает (модально) окно настроек: тема и язык.</summary>
    void Show();
}

public sealed class SettingsWindowService : ISettingsWindowService
{
    private readonly IServiceProvider _services;

    public SettingsWindowService(IServiceProvider services)
    {
        _services = services;
    }

    public void Show()
    {
        var window = _services.GetRequiredService<SettingsWindow>();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }
}
