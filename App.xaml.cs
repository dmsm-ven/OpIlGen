using Microsoft.Extensions.DependencyInjection;
using OpIlGen.Services;
using OpIlGen.Services.Transformers;
using OpIlGen.ViewModels;
using OpIlGen.Views;
using System.Windows;

namespace OpIlGen;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Тема применяется до создания окна
        _serviceProvider.GetRequiredService<IThemeService>().Apply(AppTheme.Light);

        // Главное окно создаётся через DI
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Сервисы
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IImageService, ImageService>();
        services.AddSingleton<IFullScreenService, FullScreenService>();
        services.AddSingleton<IShellService, ShellService>();
        services.AddSingleton<IThemeService, ThemeService>();

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

        // ViewModel и окна
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
