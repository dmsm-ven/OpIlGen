namespace OpIlGen.Services;

/// <summary>Настройки приложения, сохраняемые между запусками.</summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Light;
}
