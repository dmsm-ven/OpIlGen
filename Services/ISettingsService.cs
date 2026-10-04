using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpIlGen.Services;

public interface ISettingsService
{
    AppSettings Current { get; }

    /// <summary>Сохраняет текущие настройки на диск (ошибки записи не пробрасываются).</summary>
    void Save();
}

/// <summary>Хранит настройки в %APPDATA%\OpIlGen\settings.json.</summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpIlGen",
        "settings.json");

    public SettingsService()
    {
        Current = Load();
    }

    public AppSettings Current { get; }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch
        {
            // Не удалось сохранить настройки - не критично для работы приложения
        }
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                       ?? new AppSettings();
            }
        }
        catch
        {
            // Повреждённый файл - используем значения по умолчанию
        }

        return new AppSettings();
    }
}

/// <summary>Настройки приложения, сохраняемые между запусками.</summary>
public sealed class AppSettings
{
    /// <summary>Положение ползунка громкости в окне музыки (0..1, логарифмическая шкала).</summary>
    public double MusicVolumePosition { get; set; } = 0.9;

    /// <summary>Число обновлений изображения в секунду в окне музыки.</summary>
    public double MusicFps { get; set; } = 10;

    /// <summary>
    /// Привязки параметров к звуку в окне музыки: ключ преобразователя -> (ключ параметра -> название источника звука).
    /// Хранятся строками, чтобы переименование значений AudioFeature не ломало загрузку всех настроек.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> MusicMappings { get; set; } = new();

    /// <summary>Путь к последнему выбранному музыкальному файлу.</summary>
    public string? MusicLastTrack { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.Light;

    /// <summary>Код языка интерфейса (ru, en...).</summary>
    public string Language { get; set; } = OpIlGen.Localization.LocalizationService.DefaultLanguageCode;
}
