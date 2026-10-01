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
