using Microsoft.Extensions.DependencyInjection;
using OpIlGen.Localization;
using OpIlGen.Services;
using OpIlGen.Services.Transformers;
using OpIlGen.ViewModels;
using OpIlGen.Views;
using System.Windows;

namespace OpIlGen;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    /// <summary>Контейнер зависимостей (нужен расширению разметки {loc:Loc ...}).</summary>
    public IServiceProvider Services =>
        _serviceProvider ?? throw new InvalidOperationException("The service provider has not been created yet.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Сохранённые тема и язык применяются до создания окна
        var settings = _serviceProvider.GetRequiredService<ISettingsService>();
        _serviceProvider.GetRequiredService<IThemeService>().Apply(settings.Current.Theme);
        _serviceProvider.GetRequiredService<ILocalizationService>().SetLanguage(settings.Current.Language);

        // Главное окно создаётся через DI
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Сервисы
        services.AddSingleton<ILocalizationService>(_ => LocalizationService.CreateFromEmbeddedResources());
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IImageService, ImageService>();
        services.AddSingleton<IFullScreenService, FullScreenService>();
        services.AddSingleton<IShellService, ShellService>();
        services.AddSingleton<IGifService, GifService>();
        services.AddSingleton<IGifGeneratorWindowService, GifGeneratorWindowService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISettingsWindowService, SettingsWindowService>();

        ConfigureTransformers(services);


        // ViewModel и окна
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();
    }

    private static void ConfigureTransformers(IServiceCollection services)
    {
        // Преобразователи изображений. Чтобы добавить новую иллюзию -
        // создайте класс, реализующий IImageTransformer, и зарегистрируйте его здесь.
        // Порядок регистрации = порядок в списке. Первым идёт оригинал.
        services.AddSingleton<IImageTransformer, OriginalTransformer>();
        services.AddSingleton<IImageTransformer, BlackWhiteTransformer>();
        services.AddSingleton<IImageTransformer, NegativeAfterimageTransformer>();
        services.AddSingleton<IImageTransformer, PositiveAfterimageTransformer>();
        services.AddSingleton<IImageTransformer, FloydSteinbergDitheringTransformer>();
        services.AddSingleton<IImageTransformer, BayerDitheringTransformer>();
        services.AddSingleton<IImageTransformer, HalftoneTransformer>();
        services.AddSingleton<IImageTransformer, LincolnTransformer>();
        services.AddSingleton<IImageTransformer, HermannGridTransformer>();
        services.AddSingleton<IImageTransformer, CafeWallTransformer>();
        services.AddSingleton<IImageTransformer, AutokineticTransformer>();
        services.AddSingleton<IImageTransformer, PencilSketchTransformer>();
        services.AddSingleton<IImageTransformer, OilPaintingTransformer>();
        services.AddSingleton<IImageTransformer, PeripheralDriftTransformer>();
    }


    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
