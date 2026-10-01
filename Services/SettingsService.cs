using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpIlGen.Services;

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
