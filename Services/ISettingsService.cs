namespace OpIlGen.Services;

public interface ISettingsService
{
    AppSettings Current { get; }

    /// <summary>Сохраняет текущие настройки на диск (ошибки записи не пробрасываются).</summary>
    void Save();
}
