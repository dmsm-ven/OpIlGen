using System.Windows;

namespace OpIlGen.Services;

public enum AppTheme
{
    Light,
    Dark
}

public interface IThemeService
{
    AppTheme Current { get; }

    void Apply(AppTheme theme);
}

/// <summary>Переключает тему, подменяя словарь ресурсов (Themes/LightTheme.xaml / DarkTheme.xaml).</summary>
public sealed class ThemeService : IThemeService
{
    private ResourceDictionary? _currentDictionary;

    public AppTheme Current { get; private set; } = AppTheme.Light;

    public void Apply(AppTheme theme)
    {
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{theme}Theme.xaml", UriKind.Absolute)
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_currentDictionary is not null)
        {
            merged.Remove(_currentDictionary);
        }

        merged.Add(dictionary);
        _currentDictionary = dictionary;
        Current = theme;
    }
}
