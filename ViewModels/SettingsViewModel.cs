using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpIlGen.Localization;
using OpIlGen.Services;

namespace OpIlGen.ViewModels;

/// <summary>Окно настроек: выбор темы и языка. Изменения применяются сразу и сохраняются.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IThemeService _themeService;
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _localizer;
    private readonly IShellService _shell;

    public SettingsViewModel(
        IThemeService themeService, ISettingsService settings, ILocalizationService localizer, IShellService shell)
    {
        _themeService = themeService;
        _settings = settings;
        _localizer = localizer;
        _shell = shell;

        _selectedLanguage = localizer.CurrentLanguage;
    }

    // ---- Тема (два RadioButton) ----

    public bool IsLightTheme
    {
        get => _themeService.Current == AppTheme.Light;
        set { if (value) ChangeTheme(AppTheme.Light); }
    }

    public bool IsDarkTheme
    {
        get => _themeService.Current == AppTheme.Dark;
        set { if (value) ChangeTheme(AppTheme.Dark); }
    }

    private void ChangeTheme(AppTheme theme)
    {
        if (_themeService.Current == theme)
        {
            return;
        }

        _themeService.Apply(theme);

        _settings.Current.Theme = theme;
        _settings.Save();

        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    // ---- Язык (список) ----

    public IReadOnlyList<LanguageInfo> Languages => _localizer.Languages;

    [ObservableProperty]
    private LanguageInfo? _selectedLanguage;

    partial void OnSelectedLanguageChanged(LanguageInfo? value)
    {
        if (value is null || value == _localizer.CurrentLanguage)
        {
            return;
        }

        _localizer.SetLanguage(value.Code);

        _settings.Current.Language = value.Code;
        _settings.Save();
    }

    // ---- Файл настроек (только просмотр) ----

    public string SettingsFilePath => _settings.FilePath;

    [RelayCommand]
    private void OpenSettingsFile()
    {
        // Записываем текущее состояние: так файл точно существует и содержит актуальные значения
        _settings.Save();
        _shell.OpenFile(_settings.FilePath);
    }
}
